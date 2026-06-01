using AutoMapper;
using TravelTrek.Application.DTOs.TripPlanner;
using TravelTrek.Application.DTOs.Weather;
using TravelTrek.Domain.Entities.Trip;

namespace TravelTrek.Application.Mappings
{
    public class TripPlanProfile : Profile
    {
        public TripPlanProfile()
        {
            CreateMap<SaveTripPlanRequest, TripPlan>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.UserId, opt => opt.Ignore())
                .ForMember(dest => dest.User, opt => opt.Ignore());

            CreateMap<TripPlanResponse, TripPlan>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.UserId, opt => opt.Ignore())
                .ForMember(dest => dest.User, opt => opt.Ignore());

            CreateMap<DayPlanDto, DayPlan>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.TripPlanId, opt => opt.Ignore())
                .ForMember(dest => dest.TripPlan, opt => opt.Ignore());

            CreateMap<ActivityDto, Activity>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.DayPlanId, opt => opt.Ignore())
                .ForMember(dest => dest.DayPlan, opt => opt.Ignore());

            CreateMap<WeatherSummaryDto, WeatherSummary>();
            CreateMap<MealPlanDto, MealPlan>().ReverseMap();
            
            CreateMap<TripPlan, TripPlanResponse>();
            CreateMap<DayPlan, DayPlanDto>();
            CreateMap<Activity, ActivityDto>();
            CreateMap<WeatherSummary, WeatherSummaryDto>();
            CreateMap<MealPlan, MealPlanDto>();
            CreateMap<ExpenseDto, Expense>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.TripPlanId, opt => opt.Ignore())
                .ForMember(dest => dest.UserId, opt => opt.Ignore());

            CreateMap<CreateExpenseDto, Expense>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.TripPlanId, opt => opt.Ignore())
                .ForMember(dest => dest.UserId, opt => opt.Ignore());

            CreateMap<EditExpenseDto, Expense>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.TripPlanId, opt => opt.Ignore())
                .ForMember(dest => dest.UserId, opt => opt.Ignore());

            CreateMap<Expense, ExpenseDto>();
            
        }
    }
}
