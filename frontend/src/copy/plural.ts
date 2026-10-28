/** "1 clip", "3 clips": the count followed by the word that fits it. */
export function plural(count: number, one: string, many: string) {
  return `${count} ${count === 1 ? one : many}`;
}
