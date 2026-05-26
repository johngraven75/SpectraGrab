namespace SpectraGrab.Models;

public sealed record MediaFormat(
    string Id,
    string Label,
    string Extension,
    string Codec,
    string Resolution,
    string Note,
    long? FileSize);
