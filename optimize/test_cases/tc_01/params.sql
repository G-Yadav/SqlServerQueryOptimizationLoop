-- Test case 01: describe the filter scenario here (e.g., "all filters set")
-- Replace with your actual EXEC call and parameter values.
--
-- EXEC dbo.YourProc
--     @Filter1 = 'SomeValue',
--     @Filter2 = 42;
EXEC dbo.uspGetManagerEmployees @BusinessEntityID=2
EXEC dbo.uspGetManagerEmployees @BusinessEntityID=19
EXEC dbo.uspGetManagerEmployees @BusinessEntityID=3
