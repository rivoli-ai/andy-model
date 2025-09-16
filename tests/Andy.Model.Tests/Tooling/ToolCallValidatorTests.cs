using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;

namespace Andy.Model.Tests.Tooling;

public class ToolCallValidatorTests
{
    [Fact]
    public void Validate_WithValidJsonArguments_ShouldReturnValid()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "calculator",
            ArgumentsJson = "{\"expression\": \"2 + 2\"}"
        };

        var declaration = new ToolDeclaration
        {
            Name = "calculator",
            Description = "Basic calculator",
            Parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["expression"] = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    }
                },
                ["required"] = new[] { "expression" }
            }
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithInvalidJson_ShouldReturnError()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "calculator",
            ArgumentsJson = "{invalid json}"
        };

        var declaration = new ToolDeclaration
        {
            Name = "calculator",
            Description = "Basic calculator",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Invalid JSON arguments", result.Errors[0]);
    }

    [Fact]
    public void Validate_WithEmptyArguments_ShouldReturnError()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "calculator",
            ArgumentsJson = ""
        };

        var declaration = new ToolDeclaration
        {
            Name = "calculator",
            Description = "Basic calculator",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Arguments cannot be empty", result.Errors[0]);
    }

    [Fact]
    public void Validate_WithNullArguments_ShouldReturnError()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "calculator",
            ArgumentsJson = null!
        };

        var declaration = new ToolDeclaration
        {
            Name = "calculator",
            Description = "Basic calculator",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Validate_WithComplexNestedJson_ShouldReturnValid()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "search",
            ArgumentsJson = @"{
                ""query"": ""test search"",
                ""filters"": {
                    ""category"": ""tech"",
                    ""date_range"": {
                        ""start"": ""2024-01-01"",
                        ""end"": ""2024-12-31""
                    }
                },
                ""limit"": 10
            }"
        };

        var declaration = new ToolDeclaration
        {
            Name = "search",
            Description = "Search tool",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithJsonArray_ShouldReturnValid()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "batch_process",
            ArgumentsJson = @"{""items"": [""item1"", ""item2"", ""item3""]}"
        };

        var declaration = new ToolDeclaration
        {
            Name = "batch_process",
            Description = "Process multiple items",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithSpecialCharactersInJson_ShouldReturnValid()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "text_processor",
            ArgumentsJson = @"{""text"": ""Hello\nWorld\t\""quoted\""""}"
        };

        var declaration = new ToolDeclaration
        {
            Name = "text_processor",
            Description = "Process text with special characters",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithUnicodeInJson_ShouldReturnValid()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "unicode_handler",
            ArgumentsJson = @"{""text"": ""Hello 世界 😊""}"
        };

        var declaration = new ToolDeclaration
        {
            Name = "unicode_handler",
            Description = "Handle unicode text",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithNumbersInJson_ShouldReturnValid()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "math_tool",
            ArgumentsJson = @"{""integer"": 42, ""decimal"": 3.14159, ""scientific"": 1.23e-4, ""negative"": -100}"
        };

        var declaration = new ToolDeclaration
        {
            Name = "math_tool",
            Description = "Handle various number formats",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithBooleanAndNullInJson_ShouldReturnValid()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "test_call",
            Name = "config_tool",
            ArgumentsJson = @"{""enabled"": true, ""disabled"": false, ""optional"": null}"
        };

        var declaration = new ToolDeclaration
        {
            Name = "config_tool",
            Description = "Handle configuration",
            Parameters = new Dictionary<string, object>()
        };

        // Act
        var result = ToolCallValidator.Validate(toolCall, declaration);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}