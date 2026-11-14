using Npgsql;

namespace Taxi.Api.Infrastructure
{
    public static class DbExceptionExtensions
    {
        public static bool IsUniqueViolation(this Exception ex) =>
            ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
    
}