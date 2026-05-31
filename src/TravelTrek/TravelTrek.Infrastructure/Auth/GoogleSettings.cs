using System.Collections.Generic;

namespace TravelTrek.Infrastructure.Auth
{
    public class GoogleSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public List<string> AllowedAudiences { get; set; } = new();
    }
}
