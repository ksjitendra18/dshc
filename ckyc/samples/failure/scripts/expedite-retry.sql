-- ============================================================================
-- expedite-retry.sql — test-only speed-up for the retry scenarios.
--
-- The retry policy is seeded into activity_type with a HARD-CODED exponential
-- backoff starting at 24 hours (see src/CKYC.Data/Schema/Ddl.cs), so a fresh
-- failure's NextRetryAt is 24h in the future and `retry` reports
-- "none due (budget remaining + backoff elapsed)".
--
-- Running this flips the backoff to zero and makes every existing failed,
-- non-exhausted record due immediately, so a full retry/exhaustion cycle can
-- be exercised in one test sitting.  Re-run it between retry attempts.
--
-- Usage (run from the ckyc folder):
--   & 'D:\Programs\sqlite3\sqlite3.exe' .\runtime\ckyc.db ".read samples\failure\scripts\expedite-retry.sql"
-- ============================================================================

-- 1) Failed retryable records: make their next attempt due now.
--    (Status 6 = Failed; only within budget; not yet flagged for intervention.
--     '-1 day' keeps the ISO timestamp lexicographically comparable with the
--     'o'-formatted UTC timestamps the processor writes.)
UPDATE master_record
   SET NextRetryAt = strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-1 day')
 WHERE Status = 6
   AND NeedsReconcile = 0
   AND RetryCount > 0
   AND RetryCount < 3;

-- 2) Remove the backoff from every retryable activity so the NEXT failed
--    attempt is due immediately too (0 * 2^(n-1) = 0 hours).
UPDATE activity_type SET BackoffBaseHours = 0 WHERE IsRetryable = 1;