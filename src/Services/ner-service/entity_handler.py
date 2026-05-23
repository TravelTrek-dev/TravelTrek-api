"""
=============================================================================
backend/entity_handler.py
=============================================================================
Bridge between the NER model (XLM-RoBERTa) output and the XGBoost
feasibility classifier input.

Three public functions
─────────────────────
1. ner_to_features(ner_output)  → dict[9 numeric features]
2. get_diagnostic_tags(features, ner_output=None) → dict[9 binary tags]
3. explain_feasibility(label, tags, context=None) → str

Accepts NER output as EITHER:
  • Flat dict   {"LOCATION": "Paris", "DURATION": "5 days", ...}
  • Pipeline list [{"entity_group": "LOCATION", "word": "Paris"}, ...]

Does NOT import any trained model — pure parsing and mapping logic.
=============================================================================
"""

from __future__ import annotations

import re
import sys
import os
from typing import Any, Dict, List, Optional, Tuple

# ---------------------------------------------------------------------------
# Ensure project root is on sys.path so knowledge_base is importable
# regardless of whether this file is run as a script or as a module.
# ---------------------------------------------------------------------------
import sys as _sys, os as _os
_ROOT = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
if _ROOT not in _sys.path:
    _sys.path.insert(0, _ROOT)

# ---------------------------------------------------------------------------
# Knowledge-base imports (data only — no model weights)
# ---------------------------------------------------------------------------
try:
    from feasibility_model.core.knowledge_base import (
        COST_TIER,
        MIN_DAYS,
        AVOID_MONTHS,
        normalize_location,
    )
    _KB_AVAILABLE = True
except ImportError:
    # Graceful fallback when running outside the project root
    _KB_AVAILABLE = False
    COST_TIER   = {
        "paris": 4, "london": 4, "new york": 4, "tokyo": 3, "rome": 3, "marmaris": 2,
        "cannes": 4, "marseille": 3, "canne": 4, "cairo": 1, "sharm el sheikh": 2,
        "alexandria": 1, "giza": 1, "luxor": 2, "aswan": 2, "hurghada": 2, "dubai": 4,
        "bali": 1, "bangkok": 1, "phuket": 2, "lisbon": 2, "porto": 2, "barcelona": 3,
        "madrid": 3, "amsterdam": 4, "berlin": 3, "munich": 3, "vienna": 3, "prague": 2,
        "budapest": 2, "athens": 2, "santorini": 4, "mykonos": 4, "milan": 3, "venice": 4,
        "florence": 3, "singapore": 4, "sydney": 4, "melbourne": 4, "toronto": 3, "vancouver": 4
    }
    MIN_DAYS    = {
        "paris": 3, "london": 3, "tokyo": 4, "rome": 3, "new york": 4, "cairo": 2,
        "luxor": 2, "aswan": 1, "dubai": 2, "bali": 5, "bangkok": 3, "phuket": 3,
        "lisbon": 2, "porto": 2, "barcelona": 3, "madrid": 2, "amsterdam": 2,
        "berlin": 2, "munich": 2, "vienna": 2, "prague": 2, "budapest": 2,
        "athens": 2, "santorini": 2, "mykonos": 2, "milan": 2, "venice": 2,
        "florence": 2, "singapore": 2, "sydney": 4, "melbourne": 3, "toronto": 2, "vancouver": 3
    }
    AVOID_MONTHS = {
        "cairo": [6, 7, 8], "luxor": [6, 7, 8], "aswan": [6, 7, 8],
        "dubai": [6, 7, 8], "bangkok": [5, 6, 7, 8, 9, 10], "phuket": [5, 6, 7, 8, 9, 10],
        "bali": [11, 12, 1, 2, 3]
    }
    def normalize_location(name: str) -> str:
        return name.lower().strip()


# ===========================================================================
# CONSTANTS
# ===========================================================================

PEAK_MONTHS: frozenset = frozenset({6, 7, 8, 12})

# 5-tier KB scale → 3-tier model scale (matches process_real_data.py mapping)
_KB_TO_MODEL_TIER: Dict[int, int] = {1: 1, 2: 1, 3: 2, 4: 3, 5: 3}

# Representative avg daily cost (USD per person) per KB tier
# Midpoints calibrated to match training-data avg_daily_cost distribution
_KB_TIER_AVG_COST: Dict[int, float] = {
    1: 30.0,    # backpacker cheap (Vietnam, Nepal, Bali)
    2: 65.0,    # budget-friendly  (Thailand, Portugal, Mexico)
    3: 120.0,   # mid-range        (Italy, Japan, Spain, Greece)
    4: 200.0,   # expensive        (London, Paris, Dubai, Iceland)
    5: 380.0,   # ultra-luxury     (Maldives, Bora Bora, Monaco)
}

# Defaults for locations not in COST_TIER
_DEFAULT_KB_TIER       = 3          # knowledge-base tier
_DEFAULT_MODEL_TIER    = 2          # model tier (1-3)
_DEFAULT_AVG_COST      = 75.0       # as specified in the user brief

# Currency conversion to USD (hardcoded, approximate mid-2025 rates)
_CURRENCY_RATES: Dict[str, float] = {
    "usd": 1.00, "dollar": 1.00, "dollars": 1.00,
    "eur": 1.08, "euro": 1.08, "euros": 1.08,
    "gbp": 1.27, "pound": 1.27, "pounds": 1.27, "sterling": 1.27,
    "aed": 0.27, "dirham": 0.27, "dirhams": 0.27,
    "sar": 0.27, "riyal": 0.27, "riyals": 0.27,
    "inr": 0.012, "rupee": 0.012, "rupees": 0.012,
    "jpy": 0.0067, "yen": 0.0067,
    "cad": 0.74, "aud": 0.65,
    "chf": 1.12, "franc": 1.12,
    "sgd": 0.75,
    "mxn": 0.058, "peso": 0.058, "pesos": 0.058,
    "brl": 0.19, "real": 0.19, "reais": 0.19,
    "krw": 0.00075,
    "thb": 0.028, "baht": 0.028,
    "egp": 0.021,
    "mad": 0.10, "moroccan": 0.10,
}

_VAGUE_BUDGET: Dict[str, float] = {
    "low": 500.0, "tight": 500.0, "shoestring": 300.0,
    "cheap": 500.0, "budget": 800.0, "moderate": 1500.0,
    "mid": 2000.0, "medium": 2000.0, "average": 2000.0,
    "decent": 2000.0, "comfortable": 3000.0, "good": 3000.0,
    "high": 5000.0, "luxury": 6000.0, "premium": 7000.0,
    "unlimited": 10000.0,
}

# Travel type text → encoded (0=budget, 1=standard, 2=luxury)
_TRAVEL_TYPE_ENC: Dict[str, int] = {
    # budget / 0
    "budget": 0, "budget-friendly": 0, "backpacking": 0, "backpack": 0,
    "solo": 0, "solo travel": 0, "hiking": 0, "volunteer": 0,
    "road trip": 0, "eco": 0, "educational": 0, "cheap": 0,
    # standard / 1
    "standard": 1, "cultural": 1, "culture": 1, "family": 1,
    "adventure": 1, "nature": 1, "historical": 1, "history": 1,
    "food": 1, "foodie": 1, "beach": 1, "shopping": 1,
    "nightlife": 1, "photography": 1, "business": 1, "corporate": 1,
    "cruise": 1, "spiritual": 1, "groups": 1, "group": 1,
    # luxury / 2
    "luxury": 2, "honeymoon": 2, "romantic": 2, "romance": 2,
    "wellness": 2, "spa": 2, "premium": 2,
}

# Season / month text → list of month integers
_SEASON_TO_MONTHS: Dict[str, List[int]] = {
    "spring":       [3, 4, 5],
    "summer":       [6, 7, 8],
    "autumn":       [9, 10, 11],
    "fall":         [9, 10, 11],
    "winter":       [12, 1, 2],
    "rainy season": [6, 7, 8, 9],
    "dry season":   [11, 12, 1, 2, 3],
    "monsoon":      [6, 7, 8, 9],
    "eid":          [3, 4],   # approximate  (Ramadan / Eid varies)
    "christmas":    [12],
    "new year":     [12, 1],
    "easter":       [3, 4],
    "golden week":  [4, 5],
}

_MONTH_NAME_TO_INT: Dict[str, int] = {
    "january": 1, "jan": 1,
    "february": 2, "feb": 2,
    "march": 3, "mar": 3,
    "april": 4, "apr": 4,
    "may": 5,
    "june": 6, "jun": 6,
    "july": 7, "jul": 7,
    "august": 8, "aug": 8,
    "september": 9, "sep": 9, "sept": 9,
    "october": 10, "oct": 10,
    "november": 11, "nov": 11,
    "december": 12, "dec": 12,
}


# ===========================================================================
# PRIVATE PARSERS
# ===========================================================================

def _parse_duration(text: str) -> Optional[int]:
    """
    Convert natural-language duration to integer days.
    Returns None if unparseable.
    """
    if not text:
        return None
    t = text.lower().strip()

    # shorthands
    shortcuts = {
        "overnight": 1, "a day": 1, "one day": 1,
        "a weekend": 2, "long weekend": 3,
        "a week": 7, "one week": 7,
        "a fortnight": 14, "two weeks": 14,
        "a month": 30, "one month": 30,
    }
    if t in shortcuts:
        return shortcuts[t]

    # Numeric patterns: "5 days", "3 nights", "2 weeks", "1 month"
    m = re.search(
        r"(\d+(?:\.\d+)?)\s*(day|days|night|nights|week|weeks|month|months)",
        t,
    )
    if m:
        n = float(m.group(1))
        unit = m.group(2)
        if unit.startswith("week"):
            return int(round(n * 7))
        if unit.startswith("month"):
            return int(round(n * 30))
        return int(round(n))     # days or nights

    # "a/an X" pattern: "a couple of days"
    m = re.search(r"\b(a|an|one)\b", t)
    if m and "week" in t:
        return 7
    if m and "month" in t:
        return 30

    return None


def _parse_budget(text: str) -> Optional[float]:
    """
    Convert natural-language budget to USD float.
    Returns None if unparseable.
    """
    if not text:
        return None
    t = text.lower().strip()

    # Vague descriptors
    for key, usd in _VAGUE_BUDGET.items():
        if key in t:
            return usd

    # Extract numeric value and optional currency
    m = re.search(
        r"([£€\$¥₹])?\s*(\d[\d,]*(?:\.\d+)?)\s*([a-z]+)?",
        t,
    )
    if not m:
        return None

    sym    = m.group(1) or ""
    amount = float(m.group(2).replace(",", ""))
    word   = (m.group(3) or "").lower()

    sym_map = {"$": "usd", "€": "eur", "£": "gbp", "¥": "jpy", "₹": "inr"}
    currency = sym_map.get(sym, word) or "usd"

    rate = _CURRENCY_RATES.get(currency, 1.0)
    return round(amount * rate, 2)


def _parse_group_size(text: str) -> Optional[int]:
    """
    Convert natural-language group description to integer headcount.
    Returns None if unparseable.
    """
    if not text:
        return None
    t = text.lower().strip()

    shortcuts = {
        "solo": 1, "myself": 1, "just me": 1, "alone": 1,
        "a couple": 2, "couple": 2, "two": 2, "2": 2,
        "a pair": 2, "pair": 2,
    }
    if t in shortcuts:
        return shortcuts[t]

    # "family of 4", "group of 6", "party of 3"
    m = re.search(r"(?:family|group|party|team)\s+of\s+(\d+)", t)
    if m:
        return int(m.group(1))

    # "a family of 6"
    m = re.search(r"(?:a|an)\s+(?:family|group)\s+of\s+(\d+)", t)
    if m:
        return int(m.group(1))

    # "2 people", "4 adults", "3 travelers", "5 guests"
    m = re.search(r"(\d+)\s*(?:people|person|adult|adults|traveler|travellers|traveler|guest|guests|pax)", t)
    if m:
        return int(m.group(1))

    # bare digit
    m = re.match(r"^(\d+)$", t.strip())
    if m:
        return int(m.group(1))

    return None


def _parse_date_is_peak(text: str) -> Optional[int]:
    """
    Return 1 if the date phrase implies a peak-season month, 0 if off-peak,
    None if indeterminate.
    """
    if not text:
        return None
    t = text.lower().strip()

    # Direct month name
    for name, month_int in _MONTH_NAME_TO_INT.items():
        if re.search(r"\b" + name + r"\b", t):
            return 1 if month_int in PEAK_MONTHS else 0

    # Season keywords
    for season, months in _SEASON_TO_MONTHS.items():
        if season in t:
            return 1 if any(m in PEAK_MONTHS for m in months) else 0

    # Relative future ("next summer" already caught by season; "next year" etc.)
    if re.search(r"\bnext\s+(week|month|year)\b", t):
        return 0   # indeterminate, default non-peak
    if "holiday" in t or "festiv" in t:
        return 1

    return None


def _map_location(text: str) -> Tuple[int, float, bool]:
    """
    Return (location_tier[1-3], avg_daily_cost_usd, is_known).
    Uses 5-tier knowledge-base internally, maps down to 3-tier for model.
    """
    if not text:
        return _DEFAULT_MODEL_TIER, _DEFAULT_AVG_COST, False

    canonical = normalize_location(text)
    if canonical not in COST_TIER:
        return _DEFAULT_MODEL_TIER, _DEFAULT_AVG_COST, False

    kb_tier       = COST_TIER[canonical]
    model_tier    = _KB_TO_MODEL_TIER.get(kb_tier, 2)
    avg_cost      = _KB_TIER_AVG_COST.get(kb_tier, _DEFAULT_AVG_COST)
    return model_tier, avg_cost, True


def _map_travel_type(text: str) -> int:
    """Return 0/1/2 encoding; default 1 (standard) if unknown."""
    if not text:
        return 1
    t = text.lower().strip()
    # Direct lookup
    if t in _TRAVEL_TYPE_ENC:
        return _TRAVEL_TYPE_ENC[t]
    # Substring scan (e.g. "romantic getaway" → 2)
    for key, val in _TRAVEL_TYPE_ENC.items():
        if key in t:
            return val
    return 1


# ===========================================================================
# INPUT NORMALISER
# ===========================================================================

def _coalesce(ner_output: Any) -> Dict[str, str]:
    """
    Accept either:
      • flat dict {"LOCATION": "Paris", ...}
      • HuggingFace pipeline list [{"entity_group": "LOCATION", "word": "Paris"}, ...]
    Returns a flat dict, one value per entity type (first occurrence wins).
    """
    if isinstance(ner_output, dict):
        return {k.upper(): str(v) for k, v in ner_output.items() if v}

    result: Dict[str, str] = {}
    for span in ner_output:
        etype = (span.get("entity_group") or span.get("entity") or "").upper()
        # Strip BIO prefix if present (B-LOCATION → LOCATION)
        if etype.startswith(("B-", "I-")):
            etype = etype[2:]
        word = span.get("word", "")
        if etype and word and etype not in result:
            result[etype] = word
    return result


# ===========================================================================
# PUBLIC FUNCTION 1 — ner_to_features
# ===========================================================================

def ner_to_features(ner_output: Any) -> Dict[str, Any]:
    """
    Convert raw NER output into the 9 numeric features expected by the
    XGBoost feasibility model.

    Parameters
    ----------
    ner_output : dict or list
        Either ``{"LOCATION": "Paris", "DURATION": "5 days", ...}``
        or the HuggingFace pipeline list format.

    Returns
    -------
    dict
        Keys: duration_days, group_size, budget_usd, avg_daily_cost_usd,
              estimated_total_cost, budget_ratio, location_tier,
              travel_type_encoded, is_peak_season
    """
    ner = _coalesce(ner_output)

    # ── DURATION ──────────────────────────────────────────────────────────
    duration_days = _parse_duration(ner.get("DURATION", ""))
    if duration_days is None:
        duration_days = 7   # safe default: one week

    # ── GROUP SIZE ────────────────────────────────────────────────────────
    group_size = _parse_group_size(ner.get("GROUP_SIZE", ""))
    if group_size is None:
        group_size = 1      # solo traveller default

    # ── BUDGET ────────────────────────────────────────────────────────────
    budget_usd = _parse_budget(ner.get("BUDGET", ""))
    if budget_usd is None:
        budget_usd = 0.0    # missing → intentional infeasible signal

    # ── LOCATION ──────────────────────────────────────────────────────────
    location_tier, avg_daily_cost_usd, _loc_known = _map_location(ner.get("LOCATION", ""))

    # ── TRAVEL TYPE ───────────────────────────────────────────────────────
    travel_type_encoded = _map_travel_type(ner.get("TRAVEL_TYPE", ""))

    # ── DATE / SEASON ─────────────────────────────────────────────────────
    is_peak_raw = _parse_date_is_peak(ner.get("DATE", ""))
    is_peak_season = int(is_peak_raw) if is_peak_raw is not None else 0

    # ── DERIVED FEATURES ──────────────────────────────────────────────────
    estimated_total_cost = (
        float(duration_days) * float(group_size) * avg_daily_cost_usd
    )
    # Cap budget_ratio at 5.0 (matches training pre-processing)
    if estimated_total_cost > 0:
        budget_ratio = min(budget_usd / estimated_total_cost, 5.0)
    else:
        budget_ratio = 0.0

    return {
        "duration_days":        duration_days,
        "group_size":           group_size,
        "budget_usd":           round(budget_usd, 2),
        "avg_daily_cost_usd":   avg_daily_cost_usd,
        "estimated_total_cost": round(estimated_total_cost, 2),
        "budget_ratio":         round(budget_ratio, 4),
        "location_tier":        location_tier,
        "travel_type_encoded":  travel_type_encoded,
        "is_peak_season":       is_peak_season,
    }


# ===========================================================================
# PUBLIC FUNCTION 2 — get_diagnostic_tags
# ===========================================================================

def get_diagnostic_tags(
    features:   Dict[str, Any],
    ner_output: Optional[Any] = None,
) -> Dict[str, int]:
    """
    Derive the 9 binary diagnostic tags from model features and (optionally)
    the original NER output.

    Parameters
    ----------
    features   : output of ``ner_to_features``
    ner_output : original NER dict/list (improves tag accuracy for presence-
                 based tags; falls back to heuristics when None)

    Returns
    -------
    dict  All 9 tag names → 0 or 1
    """
    ner = _coalesce(ner_output) if ner_output is not None else {}

    dur  = features["duration_days"]
    grp  = features["group_size"]
    bud  = features["budget_usd"]
    br   = features["budget_ratio"]

    # ── Presence-based (accurate when ner_output is provided) ─────────────
    has_budget   = bool(ner) and "BUDGET" in ner
    has_duration = bool(ner) and "DURATION" in ner
    has_location = bool(ner) and "LOCATION" in ner

    # Fall back to feature heuristics when no NER dict is available
    if not ner:
        has_budget   = bud > 0
        has_duration = dur != 7          # 7 is the default → likely missing
        has_location = features["location_tier"] != _DEFAULT_MODEL_TIER

    # ── missing_required_info ─────────────────────────────────────────────
    # Fire when fewer than 2 "planning-relevant" entities are present
    entity_count = sum([
        has_location,
        has_duration,
        has_budget,
        bool(ner) and "GROUP_SIZE"   in ner,
        bool(ner) and "TRAVEL_TYPE"  in ner,
        bool(ner) and "DATE"         in ner,
    ]) if ner else (2 if has_location and has_budget else 1)

    missing_required_info = int(entity_count < 2)

    # ── missing_budget ────────────────────────────────────────────────────
    missing_budget = int(not has_budget)

    # ── missing_duration ─────────────────────────────────────────────────
    missing_duration = int(not has_duration)

    # ── budget_too_low ────────────────────────────────────────────────────
    # Only meaningful when budget is provided AND trip is priced
    budget_too_low = int(has_budget and 0 < br < 0.65)

    # ── duration_too_short ────────────────────────────────────────────────
    duration_too_short = 0

    # ── group_too_large ───────────────────────────────────────────────────
    group_too_large = int(grp > 8)

    # ── season_mismatch ───────────────────────────────────────────────────
    season_mismatch = 0

    # ── activity_mismatch ────────────────────────────────────────────────
    # Lightweight check: always 0 in this handler
    # (full check requires AVAILABLE_ACTIVITIES from knowledge_base — add later)
    activity_mismatch = 0

    # ── location_unknown ─────────────────────────────────────────────────
    location_unknown = 0
    if ner.get("LOCATION"):
        canonical = normalize_location(ner["LOCATION"])
        location_unknown = int(canonical not in COST_TIER)

    return {
        "missing_required_info": missing_required_info,
        "missing_budget":        missing_budget,
        "missing_duration":      missing_duration,
        "budget_too_low":        budget_too_low,
        "duration_too_short":    duration_too_short,
        "group_too_large":       group_too_large,
        "season_mismatch":       season_mismatch,
        "activity_mismatch":     activity_mismatch,
        "location_unknown":      location_unknown,
    }


def _extract_months_from_text(text: str) -> List[int]:
    """Return list of month integers mentioned in a date phrase."""
    t = text.lower()
    months: List[int] = []
    for name, num in _MONTH_NAME_TO_INT.items():
        if re.search(r"\b" + name + r"\b", t):
            months.append(num)
    for season, ms in _SEASON_TO_MONTHS.items():
        if season in t:
            months.extend(ms)
    return list(set(months))


# ===========================================================================
# PUBLIC FUNCTION 3 — explain_feasibility
# ===========================================================================

_HARD_TAGS = frozenset({"budget_too_low", "duration_too_short", "location_unknown"})

def explain_feasibility(
    label:   str,
    tags:    Dict[str, int],
    context: Optional[Dict[str, Any]] = None,
) -> str:
    """
    Produce a human-readable explanation for the feasibility verdict.

    Parameters
    ----------
    label   : "feasible" | "partial" | "infeasible"
    tags    : output of ``get_diagnostic_tags``
    context : optional dict from ``ner_to_features`` (used for richer text)
    """
    ctx = context or {}
    fired = [t for t, v in tags.items() if v]

    # ── Feasible ─────────────────────────────────────────────────────────
    if label == "feasible":
        if ctx.get("location_name"):
            avg = ctx.get("avg_daily_cost_usd", "")
            cost_str = f"~${avg:.0f}/day" if avg else ""
            loc = ctx["location_name"].title()
            return (
                f"Your trip to {loc} looks feasible! "
                + (f"Estimated daily cost is {cost_str} and " if cost_str else "")
                + f"your budget covers the full estimated cost."
            )
        return "Your trip looks feasible! The budget comfortably covers the estimated cost."

    # ── Compose explanation for partial / infeasible ──────────────────────
    parts: List[str] = []

    # Budget too low — most actionable
    if tags.get("budget_too_low"):
        br  = ctx.get("budget_ratio", 0)
        avg = ctx.get("avg_daily_cost_usd")
        loc = ctx.get("location_name", "this destination")
        pct = int(br * 100)
        cost_str = f"~${avg:.0f}/day" if avg else ""
        location_str = loc.title() if isinstance(loc, str) else "this destination"
        parts.append(
            f"Your budget is too low for this trip. "
            + (f"{location_str} costs {cost_str} " if cost_str else "")
            + f"and your budget only covers {pct}% of the estimated total cost."
        )

    # Missing budget
    if tags.get("missing_budget"):
        parts.append(
            "No budget was provided. Please specify your travel budget "
            "to get an accurate feasibility assessment."
        )

    # Missing required info
    if tags.get("missing_required_info"):
        parts.append(
            "The request is too vague to assess. Please include at least "
            "a destination and your budget."
        )

    # Duration too short
    if tags.get("duration_too_short"):
        loc = ctx.get("location_name", "this destination")
        dur = ctx.get("duration_days")
        location_str = loc.title() if isinstance(loc, str) else "This destination"
        dur_str = f"{dur} day{'s' if dur != 1 else ''}" if dur else "the stated duration"
        parts.append(
            f"{location_str} typically requires more time than {dur_str} to explore meaningfully. "
            "Consider extending your trip."
        )

    # Missing duration
    if tags.get("missing_duration"):
        parts.append(
            "No trip duration was specified. We assumed 7 days as a default. "
            "Providing the actual duration will improve this assessment."
        )

    # Group too large
    if tags.get("group_too_large"):
        grp = ctx.get("group_size")
        grp_str = f"for {grp} people " if grp else ""
        parts.append(
            f"The group size {grp_str}is unusually large for this travel type. "
            "Logistics and accommodation availability may be challenging."
        )

    # Season mismatch
    if tags.get("season_mismatch"):
        loc = ctx.get("location_name", "this destination")
        location_str = loc.title() if isinstance(loc, str) else "This destination"
        parts.append(
            f"{location_str} is generally not recommended during the requested period "
            "due to unfavourable weather or local conditions. "
            "Consider adjusting your travel dates."
        )

    # Location unknown
    if tags.get("location_unknown"):
        loc = ctx.get("location_name", "the requested location")
        parts.append(
            f"'{loc}' is not in our destination database. "
            "Cost estimates may be inaccurate — please verify pricing independently."
        )

    # Activity mismatch (placeholder)
    if tags.get("activity_mismatch"):
        parts.append(
            "One or more requested activities may not be available at this destination. "
            "Please verify activity availability before booking."
        )

    # No specific parts composed (should not normally happen)
    if not parts:
        if label == "partial":
            return (
                "Your trip is partially feasible. Some constraints are close to the limit — "
                "consider adjusting your budget or travel dates."
            )
        return (
            "This trip appears infeasible. Please review your budget, "
            "destination choice, or trip duration."
        )

    # Prefix with verdict
    verdict = "[partial]  Your trip is partially feasible." if label == "partial" else "[infeasible]  This trip is infeasible."
    return f"{verdict}\n\n" + "\n".join(f"• {p}" for p in parts)


# ===========================================================================
# CONVENIENCE WRAPPER
# ===========================================================================

def process(ner_output: Any) -> Dict[str, Any]:
    """
    Single-call convenience function.
    Returns features + tags + explanation together.

    Example
    -------
    >>> result = process({"LOCATION": "Paris", "DURATION": "5 days",
    ...                    "BUDGET": "$2000", "GROUP_SIZE": "2 people"})
    >>> print(result["explanation"])
    """
    ner   = _coalesce(ner_output)
    feats = ner_to_features(ner)
    tags  = get_diagnostic_tags(feats, ner)

    # Build context for rich explanation
    ctx = dict(feats)
    ctx["location_name"] = ner.get("LOCATION", "")

    return {
        "features":    feats,
        "tags":        tags,
        "_ner":        ner,           # retained for downstream model call
        "_context":    ctx,
    }


# ===========================================================================
# SELF-TEST
# ===========================================================================

if __name__ == "__main__":

    def _run_test(title: str, ner: dict) -> None:
        sep = "-" * 60
        print(f"\n{sep}")
        print(f"  TEST: {title}")
        print(sep)
        result = process(ner)
        feats  = result["features"]
        tags   = result["tags"]
        fired  = [t for t, v in tags.items() if v]

        print("  NER input:")
        for k, v in ner.items():
            print(f"    {k:<15} {v}")

        print("\n  Features (9 numeric):")
        for k, v in feats.items():
            print(f"    {k:<25} {v}")

        print("\n  Tags fired:")
        print(f"    {fired if fired else '(none)'}")

        # Simulate model output for explanation demo
        br = feats["budget_ratio"]
        if br == 0.0:
            label = "infeasible"
        elif br < 0.65:
            label = "infeasible"
        elif br < 1.0:
            label = "partial"
        else:
            label = "feasible"

        print(f"\n  Simulated label: {label}")
        print("\n  Explanation:")
        print(explain_feasibility(label, tags, result["_context"]))

    # ── Test 1: Feasible trip ───────────────────────────────────────────────
    _run_test(
        "Normal feasible trip — Paris, 5 days, $2000, 2 people",
        {
            "LOCATION":    "Paris",
            "DURATION":    "5 days",
            "BUDGET":      "$2000",
            "GROUP_SIZE":  "2 people",
            "TRAVEL_TYPE": "romantic",
            "DATE":        "in July",
        },
    )

    # ── Test 2: Infeasible trip ─────────────────────────────────────────────
    _run_test(
        "Infeasible trip — Paris, 10 days, $300, 3 people",
        {
            "LOCATION":    "Paris",
            "DURATION":    "10 days",
            "BUDGET":      "$300",
            "GROUP_SIZE":  "3 people",
            "TRAVEL_TYPE": "luxury",
            "DATE":        "in August",
        },
    )

    # ── Test 3: Missing budget ──────────────────────────────────────────────
    _run_test(
        "Missing budget — location only",
        {
            "LOCATION": "Bali",
        },
    )

    print("\n" + "-" * 60)
    print("  All tests completed.")
    print("-" * 60)
