namespace TextReveal.Ocr;

/// <summary>
/// One entry of the OCR language picker. <see cref="Tag"/> is the BCP-47 language tag
/// stored in <c>config.json</c>; <c>null</c> means "let Windows choose".
/// </summary>
internal sealed record OcrLanguageOption(string? Tag, string DisplayName)
{
    public override string ToString() => DisplayName;
}
