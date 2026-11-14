namespace ApiLogging.Api.Logging;

/// <summary>
/// Opt a controller or action out of API logging entirely
/// (login, token endpoints, file downloads, health checks, ...).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SkipApiLogAttribute : Attribute;

/// <summary>
/// Mask a property's value as "***" in logged request/response bodies.
/// The API itself still serializes the real value; only the log copy is masked.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SensitiveDataAttribute : Attribute;
