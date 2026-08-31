using System.Drawing;

namespace AIChat.RpaClient.Automation;

public sealed record LatestCustomerMessageOcrResult(
    CustomerMessageSnapshot? Snapshot,
    OcrResult OcrResult,
    Rectangle Region,
    bool UsedBottomUpSlice);
