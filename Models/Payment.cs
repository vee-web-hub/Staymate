using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Payment
{
    public uint PaymentId { get; set; }

    public uint BookingId { get; set; }

    public decimal Amount { get; set; }

    public string Method { get; set; } = null!;

    public string? SlipImagePath { get; set; }

    public DateTime? PaidAt { get; set; }

    public string Status { get; set; } = null!;

    public uint? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual ICollection<FinancialTransaction> FinancialTransactions { get; set; } = new List<FinancialTransaction>();

    public virtual Receipt? Receipt { get; set; }

    public virtual User? ReviewedByNavigation { get; set; }
}
