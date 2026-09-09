public sealed record AiSummaryResponse(string Summary);
public sealed record AiSuggestReplyResponse(string Suggestion);
public sealed record AiTranscribeResponse(string Text, string Language, string Provider);
public sealed record AiTranscribeErrorResponse(string Error);
public sealed record AiSummaryErrorResponse(string Error, string Message);
