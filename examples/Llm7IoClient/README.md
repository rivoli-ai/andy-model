# LLM7.io Client Example

A simple .NET client for interacting with the LLM7.io API, demonstrating both regular chat completions and tool-calling functionality.

## Features

- Lists available models from the API
- Demonstrates chat completion without tools (basic conversation)
- Demonstrates chat completion with tools (function calling)
- No external dependencies - uses only .NET built-in libraries

## Usage

```bash
cd examples/Llm7IoClient
dotnet run
```

## Example Output

The program will:

1. Fetch and display available models
2. Run a basic chat example asking for a Fibonacci function in C#
3. Run a tool-calling example where the model can call functions to:
   - Calculate Fibonacci numbers
   - Get the current time

## API Endpoints

- Base URL: `https://api.llm7.io/v1`
- `/models` - Get list of available models
- `/chat/completions` - Create chat completions

## Model Used

The example uses `gpt-4o-mini-2024-07-18` - a lightweight and fast model suitable for coding assistance tasks.

## Code Structure

- `Llm7IoClient.cs` - The main client library with API models and HTTP client
- `Program.cs` - Example usage demonstrating both regular chat and tool-calling scenarios