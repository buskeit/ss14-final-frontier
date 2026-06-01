using Content.Shared.Invoices.Systems;
using JetBrains.Annotations;

namespace Content.Client.Invoices
{
    [UsedImplicitly]
    public sealed class InvoicePrinterConsoleSystem : SharedInvoicePrinterConsoleSystem
    {
        // one day, maybe bound user interfaces can be shared too.
        // then this doesn't have to be like this.
        // I hate this.                         -the lonely commenter
    }
}
