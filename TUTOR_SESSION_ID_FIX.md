# Critical Tutor Session ID Fix - Completed

## Problem
The guided mode was failing with 404 "Tutor session not found" errors because the code was using the **chat sessionId** for the `/tutor/step` endpoint instead of the **tutor sessionId** returned from the `/tutor/start` endpoint.

## Root Cause
- `/tutor/start/{uploadId}` returns a **tutor sessionId** (e.g., `580cc1c9dd8240dea07f541d805564bd`)
- This is a separate, distinct session ID from the chat `sessionId`
- The code was incorrectly using the chat `sessionId` for all tutor requests
- The backend tutor endpoint expects the tutor sessionId, not the chat sessionId

## Solution Implemented

### Changes to `src/pages/Workspace.jsx`:

1. **Added new state for tutor session ID:**
   ```javascript
   const [tutorSessionId, setTutorSessionId] = useState(null)
   ```

2. **Updated `handleOpenGuidedMode()`:**
   - After receiving response from `startTutor()`, extract `sessionId` field from response
   - Store it in `tutorSessionId` state:
   ```javascript
   const tSessionId = step?.sessionId || null
   setTutorSessionId(tSessionId)
   ```

3. **Updated `handleTutorChoice()`:**
   - Changed guard condition from `if (!sessionId || !tutorStep)` to `if (!tutorSessionId || !tutorStep)`
   - Changed `stepTutor()` call from `stepTutor(sessionId, choiceId)` to `stepTutor(tutorSessionId, choiceId)`
   - Updated recovery logic to extract and store the new tutorSessionId:
   ```javascript
   const refreshedTutorSessionId = refreshedStart?.sessionId || null
   setTutorSessionId(refreshedTutorSessionId)
   nextStep = await stepTutor(refreshedTutorSessionId, choiceId)
   ```

## Upload ID Update

**Old (non-functional):**
```
17114DC1-3F9B-4C46-8371-F81BF5604134
```

**Current (correct):**
```
527A58FF-5ACD-45E0-A40C-935A3797B5D6
```

This upload ID is used when initiating guided mode and is passed to `POST /tutor/start/{uploadId}`.

## Flow Diagram

```
1. User clicks "Guided Mode"
   ↓
2. POST /tutor/start/{uploadId} with chat sessionId
   ↓
3. Response contains tutor sessionId → store in tutorSessionId state
   ↓
4. User clicks focus card choice
   ↓
5. POST /tutor/step with tutorSessionId (NOT chat sessionId) ✓
   ↓
6. Backend recognizes tutor session and returns next step
```

## Testing Checklist

- [ ] Guided Mode opens successfully
- [ ] Focus cards load without 404 errors
- [ ] Clicking focus card choices advances to next step
- [ ] Recovery logic triggers if tutor session becomes stale
- [ ] All UI error messages display correctly

## Code Files Modified
- `src/pages/Workspace.jsx` - Added tutorSessionId state and updated handlers
- No changes needed to `src/lib/api.js` (stepTutor signature remains the same)
- No changes needed to `src/components/GuidedModeFlow.jsx`
