namespace Arcvora.Infrastructure.Common;

/// <summary>API'ye eşlenen uygulama hatası. <see cref="Code"/> makine tarafından okunur.</summary>
public class AppException(int status, string code, string message, object? details = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

public sealed class NotFoundException(string what = "Kayıt bulunamadı.")
    : AppException(404, "not_found", what);

public sealed class ForbiddenException(string message = "Bu işlem için yetkiniz yok.")
    : AppException(403, "forbidden", message);

public sealed class ConflictException(string code, string message, object? details = null)
    : AppException(409, code, message, details);

public sealed class RequestValidationException(string code, string message, object? details = null)
    : AppException(422, code, message, details);

public sealed class BadRequestException(string message, string code = "bad_request")
    : AppException(400, code, message);
