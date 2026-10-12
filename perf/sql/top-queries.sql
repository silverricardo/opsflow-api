-- The most expensive statements since the plan cache was last cleared (run after a load test).
-- Reports average CPU, elapsed time and logical reads; compare runs before/after an index or query change.
USE OpsFlowPerf;
SET NOCOUNT ON;

SELECT TOP (12)
    qs.execution_count                                                           AS executions,
    CAST(qs.total_worker_time / 1000.0 / qs.execution_count AS decimal(10, 2))   AS avg_cpu_ms,
    CAST(qs.total_elapsed_time / 1000.0 / qs.execution_count AS decimal(10, 2))  AS avg_elapsed_ms,
    qs.total_logical_reads / qs.execution_count                                  AS avg_logical_reads,
    LEFT(REPLACE(REPLACE(SUBSTRING(st.text, qs.statement_start_offset / 2 + 1,
        (CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END
         - qs.statement_start_offset) / 2 + 1), CHAR(10), ' '), CHAR(13), ' '), 220) AS statement
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
-- Parameterised statements (sp_executesql) have no dbid in the SQL text: take it from the plan.
CROSS APPLY (SELECT CONVERT(int, value) AS dbid FROM sys.dm_exec_plan_attributes(qs.plan_handle) WHERE attribute = 'dbid') AS pa
WHERE pa.dbid = DB_ID()
ORDER BY qs.total_worker_time DESC;

-- Index usage: seeks vs scans per index of the main tables.
SELECT OBJECT_NAME(s.object_id) AS [table], i.name AS [index], s.user_seeks, s.user_scans, s.user_lookups, s.user_updates
FROM sys.dm_db_index_usage_stats AS s
JOIN sys.indexes AS i ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE s.database_id = DB_ID() AND OBJECT_NAME(s.object_id) IN ('Requests', 'ApprovalSteps', 'AuditEntries', 'Users')
ORDER BY [table], s.user_seeks + s.user_scans DESC;
