using System;
using TinyBus;

namespace TinyBus.Sample.Contracts;

public sealed record CapturePayment(
    Guid PaymentId,
    decimal Amount);

[BusContract("orders.order-placed")]
public sealed record OrderPlaced(Guid OrderId);

public sealed record GetPaymentStatus(Guid PaymentId);

public sealed record PaymentStatus(
    Guid PaymentId,
    string Status);
