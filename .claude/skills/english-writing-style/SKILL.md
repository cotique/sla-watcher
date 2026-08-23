---
name: english-writing-style
description: Default-language and writing-style rules for code, comments, commits, docs, and English chat responses. Use this skill any time you are about to write code, code comments, commit messages, PR descriptions, technical documentation, or an English chat reply. It enforces two things: (1) code / comments / commits / technical docs default to English unless the user explicitly asks for another language, and (2) English output is semi-casual, with no formal C2 vocabulary, no literary or artistic flourishes, and no em dashes. Trigger even when the user is writing in another language, because the default language for code artifacts is still English.
---

# English Writing Style

This skill covers two things: when to write in English, and how to write English well.

## When to write in English

Default to English for all technical artifacts:

- Code (identifiers, strings in the code, log messages)
- Code comments
- Commit messages, PR titles and descriptions
- Technical documentation (README, architecture docs, API docs, plan files)
- Inline TODO / FIXME notes

This default applies even when the conversation with the user is in another language. Communication with the user happens in their language, but code artifacts stay in English so the codebase reads consistently for any future contributor.

Override the default only when the user explicitly asks for another language for a specific artifact (for example, "write this README in French"). A one-off user comment in another language inside a chat message is not an instruction to switch the codebase language.

If you are unsure whether a string belongs to "user-facing UI copy" (which may need localization) or "internal technical text" (which stays English), ask. UI copy for end users is a product concern and usually goes through the project's i18n system, not through ad-hoc translation.

## Tone (for English output)

Write semi-casual. Match how a knowledgeable colleague explains something in chat, not how a textbook or essay phrases it.

## Avoid

- **Formal C2 vocabulary.** Skip words that signal academic or literary register: "utilize", "endeavor", "furthermore", "nonetheless", "aforementioned", "henceforth", "pertaining to", "in lieu of", "with regard to", "leverage" (as a verb), "facilitate", "commence". Pick the common word instead: "use", "try", "also", "still", "that", "from now on", "about", "instead of", "help", "start".
- **Literary, artistic, or expressive language.** No decorative metaphors, no rhetorical flourishes, no dramatic phrasing. "The results were illuminating" becomes "the results were useful". "A symphony of errors" becomes "a lot of errors". "Elegant" and "beautiful" about code are fine only when they describe something specific and technical.
- **Em dashes (—).** Never. Use a period, comma, colon, parentheses, or "and"/"but" instead. This applies to output in files too, not just chat.

## Keep

- Contractions (it's, don't, you're, we'll). They read natural in semi-casual tone.
- Plain structure. Short sentences are fine. Longer ones are fine too if they carry their weight.
- Technical precision. Semi-casual does not mean vague. Say exactly what you mean with ordinary words.

## Why this matters

Overly formal or expressive English feels distant and slows the reader down. Plain, conversational prose lands faster and reads like a real person wrote it. The goal is clarity without stiffness. Em dashes specifically are an AI tell and the user wants them gone.

## Examples

**Too formal:**
> Furthermore, it is imperative that we utilize the aforementioned methodology to ascertain the root cause.

**Semi-casual:**
> Also, we need to use that approach to find the root cause.

**Too literary:**
> The migration, a sprawling beast of tangled dependencies, finally crossed the finish line.

**Semi-casual:**
> The migration had a lot of dependencies, but it finally shipped.

**Em dash (do not write):**
> The bug was subtle — it only triggered under concurrent writes.

**Replacement:**
> The bug was subtle. It only triggered under concurrent writes.

**Another em dash (do not write):**
> Three options here — pick whichever fits.

**Replacement (colon or period):**
> Three options here. Pick whichever fits.
