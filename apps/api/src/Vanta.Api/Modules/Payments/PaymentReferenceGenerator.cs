using System.Security.Cryptography;

namespace Vanta.Api.Modules.Payments;

// Generates the opaque public reference exposed in customer-facing checkout URLs/messages.
// Deliberately unrelated to any provider's own transaction id so it never leaks provider
// internals and cannot be guessed or enumerated.
public static class PaymentReferenceGenerator
{
    public static string Generate() => RandomNumberGenerator.GetHexString(32, lowercase: true);
}
