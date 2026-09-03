/**
 * Renders an event's calendar day in Polish, e.g. "12 września 2026".
 *
 * The API sends a DateOnly, which serialises to "2026-09-12". That string is deliberately
 * never handed to `new Date(...)` directly: the single-argument form parses a bare
 * yyyy-mm-dd as midnight UTC, so anywhere west of Greenwich it renders as the day before.
 * Building the date from explicit parts sidesteps the whole class of bug, and doing the
 * formatting here rather than through Angular's DatePipe also avoids having to register a
 * locale just to stop it printing "September 12, 2026".
 */
export function formatEventDate(isoDate: string | null | undefined): string {
  if (!isoDate) return '';

  const parts = /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate);
  if (!parts) return '';

  const [, year, month, day] = parts;
  const date = new Date(Number(year), Number(month) - 1, Number(day));

  return new Intl.DateTimeFormat('pl-PL', {
    day: 'numeric',
    month: 'long',
    year: 'numeric'
  }).format(date);
}
