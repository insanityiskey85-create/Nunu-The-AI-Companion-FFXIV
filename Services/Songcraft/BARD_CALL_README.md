# Bard-Call in Chat

Adds a helper to trigger Songcraft when the message contains a trigger token (default: `/song`).

## Wire-up
Call this at the very start of your user message handler:
```csharp
if (TryHandleBardCall(author, text))
    return;
```

**Examples**
- `@nunu /song` → auto mood
- `@nunu /song sorrow hush the storm`
- `@nunu sing… /song triumph we did it!`
