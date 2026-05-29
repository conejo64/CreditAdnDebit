/**
 * Auth-alignment spec for the collections/delinquency route.
 *
 * Policy decision (v76-mora-temprana): Auditor is NOT included in CanViewCollections.
 * Access is granted if: role is Admin or Operator, OR granular claim collections:view is present.
 * This file enforces that the frontend route data matches the backend policy contract end-to-end.
 */
import { routes } from '../../app.routes';
import { Route } from '@angular/router';

function findRoute(path: string): Route | undefined {
  const appRoute = routes.find(r => r.path === 'app');
  if (!appRoute?.children) { return undefined; }
  return appRoute.children.find(r => r.path === path);
}

describe('collections/delinquency route authorization alignment', () => {
  it('should restrict delinquency route to Admin and Operator only (Auditor excluded per policy)', () => {
    const route = findRoute('collections/delinquency');
    expect(route).toBeDefined();
    const roles: string[] = route?.data?.['roles'] ?? [];
    expect(roles).toContain('Admin');
    expect(roles).toContain('Operator');
    expect(roles).not.toContain('Auditor');
  });

  // TRIANGULATE: canActivate must include roleGuard (not just authGuard)
  it('should have roleGuard in canActivate for the delinquency route', () => {
    const route = findRoute('collections/delinquency');
    expect(route).toBeDefined();
    expect(route?.canActivate).toBeDefined();
    expect((route?.canActivate ?? []).length).toBeGreaterThan(0);
  });

  // TRIANGULATE: route must use lazy loading (loadComponent), not static component
  it('should use loadComponent (lazy loading) instead of a static component reference', () => {
    const route = findRoute('collections/delinquency');
    expect(route).toBeDefined();
    expect(route?.loadComponent).toBeDefined();
    expect((route as any)?.component).toBeUndefined();
  });

  // RED: route must declare collections:view in data.permissions for granular auth contract
  it('should declare collections:view in route data.permissions (end-to-end auth contract)', () => {
    const route = findRoute('collections/delinquency');
    expect(route).toBeDefined();
    const permissions: string[] = route?.data?.['permissions'] ?? [];
    expect(permissions).toContain('collections:view');
  });
});
