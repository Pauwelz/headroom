namespace Headroom;

// A rule that refuses a change, apart from how it is sent. REST sends it as a problem. MCP throws McpException(Detail).
public record Refusal(int Status, string Detail);
