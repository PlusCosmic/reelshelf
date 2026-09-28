/**
 * A night of play runs past midnight, so a gaming session's day starts at 5am: a clip at 2am
 * belongs to the evening before. Uploads file clips into session collections by this day, and the
 * clip page's "Same session" uses the same bounds, so the two always agree.
 */
const SESSION_DAY_STARTS_AT_HOUR = 5;

function formatLocalDate(date: Date) {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, "0");
  const day = `${date.getDate()}`.padStart(2, "0");
  return `${year}-${month}-${day}`;
}

/** The session's calendar day at local midnight, e.g. 2am on the 11th gives the 10th. */
function sessionDay(date: Date) {
  const day = new Date(date);
  if (day.getHours() < SESSION_DAY_STARTS_AT_HOUR)
    day.setDate(day.getDate() - 1);
  day.setHours(0, 0, 0, 0);
  return day;
}

/** The session a moment belongs to, as a local `YYYY-MM-DD` date. */
export function gamingSessionDate(date: Date) {
  return formatLocalDate(sessionDay(date));
}

/** When the session a moment belongs to begins and ends: 5am on its day to 5am the next. */
export function gamingSessionBounds(date: Date) {
  const day = sessionDay(date);
  const start = new Date(day);
  start.setHours(SESSION_DAY_STARTS_AT_HOUR);
  const end = new Date(day);
  end.setDate(end.getDate() + 1);
  end.setHours(SESSION_DAY_STARTS_AT_HOUR);
  return { day, start, end };
}
