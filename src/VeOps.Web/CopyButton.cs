namespace VeOps.Web;

/// <summary>
/// Model for <c>Pages/Shared/_CopyButton.cshtml</c> — a quiet icon button that puts
/// <paramref name="Value"/> on the clipboard.
/// </summary>
/// <param name="Value">
/// The text to copy. Rendered into a <c>data-copy-value</c> attribute and read by
/// <c>app.js</c>; null or blank renders nothing at all, so a call site does not need its own
/// guard around a missing figure.
/// </param>
/// <param name="What">
/// What the value is, for the accessible label — "FRN" gives "Copy FRN 0038689741". An icon
/// with no label is a guess for a sighted user and invisible to a screen reader.
/// </param>
public record CopyButton(string? Value, string What);
