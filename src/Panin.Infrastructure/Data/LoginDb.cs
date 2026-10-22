using Panin.Application.Abstractions;

namespace Panin.Infrastructure.Data;

public sealed class LoginDb(string connectionString) : SqlDb(connectionString), ILoginDb;
