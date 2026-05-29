/**
 * Unit tests for the role guard's authorization logic.
 *
 * Auth contract (v76-mora-temprana):
 *   CanViewCollections backend policy allows:
 *     - role: Admin
 *     - role: Operator
 *     - granular claim: collections:view
 *
 * The frontend guard must honour the same contract:
 *   isRouteAuthorized(user, allowedRoles, requiredPermissions)
 *   → true  if user has ANY of allowedRoles
 *   → true  if user has ANY of requiredPermissions
 *   → false otherwise
 */
import { isRouteAuthorized } from './role.guard';
import { User } from '../auth.service';

function makeUser(roles: string[], permissions: string[] = []): User {
  return { id: '1', name: 'Test', email: 'test@test.com', role: roles[0] ?? '', roles, permissions };
}

describe('isRouteAuthorized — end-to-end auth contract', () => {
  const allowedRoles = ['Admin', 'Operator'];
  const requiredPermissions = ['collections:view'];

  // --- Role-based access (existing contract) ---

  it('should allow access for Admin role', () => {
    const user = makeUser(['Admin']);
    expect(isRouteAuthorized(user, allowedRoles, requiredPermissions)).toBeTrue();
  });

  it('should allow access for Operator role', () => {
    const user = makeUser(['Operator']);
    expect(isRouteAuthorized(user, allowedRoles, requiredPermissions)).toBeTrue();
  });

  it('should deny access when role is Auditor and no granular permission', () => {
    const user = makeUser(['Auditor']);
    expect(isRouteAuthorized(user, allowedRoles, requiredPermissions)).toBeFalse();
  });

  // --- Granular permission fallback (new contract, closes end-to-end gap) ---

  it('should allow access when user has granular collections:view permission even without Admin/Operator role', () => {
    const user = makeUser(['Auditor'], ['collections:view']);
    expect(isRouteAuthorized(user, allowedRoles, requiredPermissions)).toBeTrue();
  });

  it('should allow access when user has collections:view permission with no role match', () => {
    const user = makeUser(['ReadOnly'], ['collections:view']);
    expect(isRouteAuthorized(user, allowedRoles, requiredPermissions)).toBeTrue();
  });

  // --- Edge cases ---

  it('should deny access when user is null', () => {
    expect(isRouteAuthorized(null, allowedRoles, requiredPermissions)).toBeFalse();
  });

  it('should allow access when allowedRoles is empty (open route) regardless of permissions', () => {
    const user = makeUser(['ReadOnly']);
    expect(isRouteAuthorized(user, [], [])).toBeTrue();
  });

  it('should deny access when user has neither matching role nor matching permission', () => {
    const user = makeUser(['ReadOnly'], ['transactions:view']);
    expect(isRouteAuthorized(user, allowedRoles, requiredPermissions)).toBeFalse();
  });
});
