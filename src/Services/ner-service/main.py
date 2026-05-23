from fastapi import FastAPI
from pydantic import BaseModel
from transformers import AutoTokenizer, AutoModelForTokenClassification, pipeline
import pickle
import pandas as pd
from typing import Any
from entity_handler import process, explain_feasibility

app = FastAPI()

model_path = "hossamaladdin/ai-travel-planner-ner"
tokenizer = AutoTokenizer.from_pretrained(model_path)
model = AutoModelForTokenClassification.from_pretrained(model_path)
ner = pipeline("ner", model=model, tokenizer=tokenizer, aggregation_strategy="simple")

# Load the feasibility model and label encoder
feasibility_model = pickle.load(open("feasibility_model.pkl", "rb"))
label_encoder = pickle.load(open("label_encoder.pkl", "rb"))

class Request(BaseModel):
    inputs: str

class FeasibilityRequest(BaseModel):
    ner_output: Any

@app.post("/extract")
def extract(req: Request):
    results = ner(req.inputs)
    return [
        {
            "entity_group": r["entity_group"],
            "score": float(r["score"]),
            "word": r["word"],
            "start": int(r["start"]),
            "end": int(r["end"]),
        }
        for r in results
    ]

@app.post("/check-feasibility")
def check_feasibility(req: FeasibilityRequest):
    res = process(req.ner_output)
    feats = res["features"]
    tags = res["tags"]
    
    # XGBoost features order
    feature_names = [
        'duration_days', 'group_size', 'budget_usd', 'avg_daily_cost_usd',
        'estimated_total_cost', 'budget_ratio', 'location_tier',
        'travel_type_encoded', 'is_peak_season'
    ]
    df = pd.DataFrame([feats])[feature_names]
    
    # Predict
    pred_idx = feasibility_model.predict(df)[0]
    label = label_encoder.inverse_transform([pred_idx])[0]
    explanation = explain_feasibility(label, tags, res["_context"])
    
    # If predicted as infeasible/partial, but the ONLY issue is advisory (like a short duration,
    # off-season, or missing budget), we should consider it feasible and let the LLM generate the plan.
    # We only treat "budget_too_low" as a fatal blocker (mathematically impossible to complete).
    is_actually_feasible = (label == "feasible")
    if not is_actually_feasible:
        fatal_blockers = [
            tags.get("budget_too_low", 0)
        ]
        if not any(fatal_blockers):
            is_actually_feasible = True

    # Strip bracket prefixes like [partial] or [infeasible] if any for clean presentation
    clean_explanation = explanation
    if clean_explanation.startswith("["):
        parts = clean_explanation.split("]", 1)
        if len(parts) > 1:
            clean_explanation = parts[1].strip()
            
    # If the trip is blocked by other fatal constraints, clean up the optional 'No budget was provided' noise
    if not is_actually_feasible and tags.get("missing_budget"):
        raw_lines = clean_explanation.split("\n")
        filtered_lines = [line for line in raw_lines if "No budget was" not in line]
        cleaned_lines = []
        for line in filtered_lines:
            if not line.strip() and cleaned_lines and not cleaned_lines[-1].strip():
                continue
            cleaned_lines.append(line)
        clean_explanation = "\n".join(cleaned_lines).strip()
            
    return {
        "verdict": label,
        "is_feasible": is_actually_feasible,
        "explanation": clean_explanation,
        "features": feats,
        "tags": {t: bool(v) for t, v in tags.items()}
    }