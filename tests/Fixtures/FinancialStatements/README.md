# Financial statement test contracts

These fixed report payloads were captured from the existing statement component
fixtures before separating the Web and integration suites on 10 October 2026.
They retain the original account rows, comparisons, KPI values, and CSV content.

Web.Tests renders these payloads without invoking backend transformations.
Finance.Tests builds the same examples through the production layout/catalog and
CSV formatter and checks the complete payload against these contracts. Both
projects copy the JSON into their own test output; neither shares executable
test sources or references the other project.

For an intentional layout or wire change, review these fixtures together with
the backend contract assertions and component expectations. Do not regenerate
them solely to make a failing test pass.
