## File headers
Add this header at the top of every new source file, using that file type's comment syntax. Skip JSON, lock files, .md and .gitignore.

```
// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "<one-line summary of the prompt that produced this file>"
// AI Contribution: <what you wrote> (~100%)
// Human Modifications: None yet
// Verification: <tests or checks you ran that cover this file>
// Confidence: <Low / Medium / High>
```

If you change a file that still has the older one-line `AI Contribution Weight` header, replace it with this one.

## Task notes
After each task, write a note in the notes folder I've added to this session
(the one ending in `MyAI/PAx`). Name it `YYYY-MM-DD_NN_short-name.md` and use these headings:
What I was asked, What I did, Files created / changed, Decisions I made (and why),
Problems / things I couldn't do, Commands run / test results.
Keep it short and factual. Don't read other files in that folder unless I ask.