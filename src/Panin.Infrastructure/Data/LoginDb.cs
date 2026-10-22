using Panin.Application.Abstractions;

namespace Panin.Infrastructure.Data;

public sealed class LoginDb(string connectionString, IQueryObserver? observer = null) : SqlDb(connectionString, "login", observer), ILoginDb;
