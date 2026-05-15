namespace TravelTrek.Domain.Common
{
    /// <summary>
    /// Represents an error with code, description and type
    /// </summary>
    public sealed class Error : IEquatable<Error>
    {
        public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Validation);

        public Error(string code, string description, ErrorType type)
        {
            Code = code;
            Description = description;
            Type = type;
        }

        /// <summary>
        /// Unique error code (e.g., "User.NotFound")
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Human-readable error description
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Category/Type of the error
        /// </summary>
        public ErrorType Type { get; }

        /// <summary>
        /// Creates a validation error
        /// </summary>
        public static Error Validation(string code, string description) =>
            new(code, description, ErrorType.Validation);

        /// <summary>
        /// Creates a not found error
        /// </summary>
        public static Error NotFound(string code, string description) =>
            new(code, description, ErrorType.NotFound);

        /// <summary>
        /// Creates a conflict error
        /// </summary>
        public static Error Conflict(string code, string description) =>
            new(code, description, ErrorType.Conflict);

        /// <summary>
        /// Creates an unauthorized error
        /// </summary>
        public static Error Unauthorized(string code, string description) =>
            new(code, description, ErrorType.Unauthorized);

        /// <summary>
        /// Creates a forbidden error
        /// </summary>
        public static Error Forbidden(string code, string description) =>
            new(code, description, ErrorType.Forbidden);

        /// <summary>
        /// Creates an internal server error
        /// </summary>
        public static Error Internal(string code, string description) =>
            new(code, description, ErrorType.Internal);

        /// <summary>
        /// Creates an external service error
        /// </summary>
        public static Error External(string code, string description) =>
            new(code, description, ErrorType.External);
        
        public static Error TooManyRequests(string code, string description) =>
            new(code, description, ErrorType.TooManyRequests);

        public bool Equals(Error? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Code == other.Code && Type == other.Type;
        }

        public override bool Equals(object? obj) => obj is Error error && Equals(error);

        public override int GetHashCode() => HashCode.Combine(Code, Type);

        public static bool operator ==(Error? left, Error? right) =>
            left is null && right is null || left is not null && left.Equals(right);

        public static bool operator !=(Error? left, Error? right) => !(left == right);

        public override string ToString() => $"[{Type}] {Code}: {Description}";
    }
}