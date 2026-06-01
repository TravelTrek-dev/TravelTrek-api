using AutoMapper;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.Interfaces;
using TravelTrek.Domain.Common;
using TravelTrek.Domain.Entities.Trip;
using TravelTrek.Domain.Interfaces;

namespace TravelTrek.Application.Services;

public class TripExpenseService : ITripExpenseService
{
    private readonly ITripPlanRepository _tripPlanRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public TripExpenseService(ITripPlanRepository tripPlanRepository, IExpenseRepository expenseRepository, IUnitOfWork unitOfWork, IMapper mapper)
    {
        _tripPlanRepository = tripPlanRepository;
        _expenseRepository = expenseRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<Guid>> AddTripExpenseAsync(CreateExpenseDto request, Guid tripPlanId, Guid userId, CancellationToken ct = default)
    {
        var tripPlan = await _tripPlanRepository.GetByIdAsync(tripPlanId);
        if (tripPlan == null)
        {
            return Result.Failure<Guid>(Error.NotFound("AddTripExpense.NotFound", "Trip does not exist"));
        }

        if (tripPlan.UserId != userId)
        {
            return Result.Failure<Guid>(Error.Forbidden("AddTripExpense.Forbidden", "Forbidden request"));
        }
        
        var newExpense = new Expense()
        {
            TripPlanId = tripPlanId,
            UserId = userId
        };

        _mapper.Map(request, newExpense);

        await _expenseRepository.AddAsync(newExpense);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success(newExpense.Id);
    }
    
    public async Task<Result> EditTripExpenseAsync(EditExpenseDto request, Guid id, Guid userId, CancellationToken ct = default)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        if (expense == null)
        {
            return Result.Failure(Error.NotFound("EditTripExpense.NotFound", "Expense does not exist"));
        }
        if (expense.UserId != userId)
        {
            return Result.Failure(Error.Forbidden("EditTripExpense.Forbidden", "Forbidden request"));
        }

        _mapper.Map(request, expense);

        _expenseRepository.Update(expense);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> DeleteTripExpenseAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        if (expense == null)
        {
            return Result.Failure(Error.NotFound("DeleteTripExpense.NotFound", "Expense does not exist"));
        }
        if (expense.UserId != userId)
        {
            return Result.Failure(Error.Forbidden("DeleteTripExpense.Forbidden", "Forbidden request"));
        }
        
        _expenseRepository.Delete(expense);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }
    
    public async Task<Result<ExpensesDto>> GetTripExpensesAsync(Guid tripPlanId, Guid userId, CancellationToken ct = default)
    {
        var tripPlan = await _tripPlanRepository.GetByIdAsync(tripPlanId);
        if (tripPlan == null)
        {
            return Result.Failure<ExpensesDto>(Error.NotFound("GetTripExpenses.NotFound", "Trip does not exist"));
        }

        if (tripPlan.UserId != userId)
        {
            return Result.Failure<ExpensesDto>(Error.Forbidden("GetTripExpenses.Forbidden", "Forbidden request"));
        }
        var expenses = await _expenseRepository.GetForTrip(tripPlanId);

        var spent = expenses.Sum(e => e.Price);
        var remaining = tripPlan.Budget - spent;
        var remainingValue = remaining ?? 0m;
        var expensesDto = new ExpensesDto()
        {
            City = tripPlan.City,
            Expenses = _mapper.Map<List<ExpenseDto>>(expenses),
            Budget = tripPlan.Budget ?? 0m,
            Spent  = spent,
            Remaining = remainingValue > 0 ? remainingValue : 0m,
            Currency = tripPlan.Currency ?? ""
        };

        return Result.Success(expensesDto);
    }
}
