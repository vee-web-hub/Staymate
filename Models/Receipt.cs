using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Receipt
{
    public uint ReceiptId { get; set; }

    public string ReceiptNo { get; set; } = null!;

    public uint PaymentId { get; set; }

    public decimal Amount { get; set; }

    public uint? IssuedBy { get; set; }

    public DateTime IssuedAt { get; set; }

    public virtual User? IssuedByNavigation { get; set; }

    public virtual Payment Payment { get; set; } = null!;
}
