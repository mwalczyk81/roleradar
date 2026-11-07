/**
 * Compile-time completeness for UI-ordered enum lists.
 *
 * Enum membership comes from the generated API types (`contracts/client/api-types.ts`); what the UI adds is the order
 * and the labels. `exhaustiveOrder<T>()([...])` returns the list unchanged, but it is a compile error when a member of
 * `T` is missing, so a value added to the API cannot be silently absent from a dropdown. Members outside `T` are
 * rejected by the element type. A repeated member is caught by `hasUniqueMembers` in the unit tests.
 */
export function exhaustiveOrder<T extends string>() {
  return <const L extends readonly T[]>(
    order: L,
    ..._complete: Exclude<T, L[number]> extends never ? [] : [missing: Exclude<T, L[number]>]
  ): L => order;
}

export function hasUniqueMembers(list: readonly string[]): boolean {
  return new Set(list).size === list.length;
}
