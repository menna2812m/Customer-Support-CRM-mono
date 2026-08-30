/**
 * One page of a collection, mirroring the shared pagination contract
 * (specs/001-project-foundation/contracts/pagination-contract.md).
 *
 * In core rather than in a feature because every list endpoint in the product returns this shape.
 * Feature 003 declared it locally when it was the only consumer; feature 004 made it the second,
 * and a second copy of a published contract is a second thing that can drift from it.
 */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
