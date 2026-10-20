using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class FinancialTransaction
{
    public uint TxnId { get; set; }

    public string TxnType { get; set; } = null!;

    public string Category { get; set; } = null!;

    public decimal Amount { get; set; }

    public uint? PaymentId { get; set; }

    public DateOnly TxnDate { get; set; }

    public string? Description { get; set; }

    public uint? RecordedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Payment? Payment { get; set; }

    public virtual User? RecordedByNavigation { get; set; }
}
