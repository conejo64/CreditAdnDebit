/**
 * Sidebar safety-net spec for v76-mora-temprana.
 *
 * Policy decision: Cobranzas/Mora Temprana is restricted to Admin + Operator only,
 * OR a user with the granular `collections:view` permission claim.
 * This spec prevents role-regression AND enforces the end-to-end auth contract.
 */
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SidebarComponent } from './sidebar.component';
import { AuthService } from '../../core/auth.service';
import { User } from '../../core/auth.service';
import { signal } from '@angular/core';

function findMoraTemprana(component: SidebarComponent) {
  const cobranzas = component.menuGroups.find(g => g.title === 'Cobranzas');
  return cobranzas?.items.find(item => item.route === '/app/collections/delinquency');
}

describe('SidebarComponent — Cobranzas group safety net', () => {
  let component: SidebarComponent;

  beforeEach(async () => {
    const authServiceMock = {
      currentUser: signal(null)
    };

    await TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceMock }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
  });

  // RED→GREEN: Cobranzas group must exist with Mora Temprana entry
  it('should have a Cobranzas nav group with a Mora Temprana entry', () => {
    const cobranzas = component.menuGroups.find(g => g.title === 'Cobranzas');
    expect(cobranzas).toBeDefined();
    const item = findMoraTemprana(component);
    expect(item).toBeDefined();
    expect(item?.label).toBe('Mora Temprana');
  });

  // RED→GREEN: Mora Temprana must NOT include Auditor
  it('should restrict Mora Temprana to Admin and Operator — Auditor excluded', () => {
    const item = findMoraTemprana(component);
    expect(item).toBeDefined();
    const roles: string[] = item?.roles ?? [];
    expect(roles).toContain('Admin');
    expect(roles).toContain('Operator');
    expect(roles).not.toContain('Auditor');
  });

  // TRIANGULATE: verify icon and route are correct
  it('should use the warning icon and the correct route for Mora Temprana', () => {
    const item = findMoraTemprana(component);
    expect(item?.icon).toBe('warning');
    expect(item?.route).toBe('/app/collections/delinquency');
  });

  // TRIANGULATE: collections:view permission claim declared in sidebar item
  it('should declare collections:view in the permissions list of Mora Temprana sidebar item', () => {
    const item = findMoraTemprana(component);
    expect(item).toBeDefined();
    const permissions: string[] = (item as any)?.permissions ?? [];
    expect(permissions).toContain('collections:view');
  });
});

describe('SidebarComponent — canAccess granular permission contract', () => {
  function makeUser(roles: string[], permissions: string[] = []): User {
    return { id: '1', name: 'Test', email: 'test@test.com', role: roles[0] ?? '', roles, permissions };
  }

  async function buildComponent(user: User | null): Promise<SidebarComponent> {
    const authServiceMock = { currentUser: signal(user) };
    await TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceMock }
      ]
    }).compileComponents();
    return TestBed.createComponent(SidebarComponent).componentInstance;
  }

  afterEach(() => TestBed.resetTestingModule());

  // RED: canAccess must return true for user with collections:view permission even without role match
  it('should grant canAccess for Mora Temprana when user has collections:view permission (no role match)', async () => {
    const component = await buildComponent(makeUser(['Auditor'], ['collections:view']));
    const item = component.menuGroups
      .find(g => g.title === 'Cobranzas')?.items
      .find(i => i.route === '/app/collections/delinquency');
    expect(item).toBeDefined();
    expect(component.canAccess(item!)).toBeTrue();
  });

  // TRIANGULATE: canAccess must deny when neither role nor permission matches
  it('should deny canAccess for Mora Temprana when user has neither matching role nor permission', async () => {
    const component = await buildComponent(makeUser(['Auditor'], ['transactions:view']));
    const item = component.menuGroups
      .find(g => g.title === 'Cobranzas')?.items
      .find(i => i.route === '/app/collections/delinquency');
    expect(item).toBeDefined();
    expect(component.canAccess(item!)).toBeFalse();
  });

  // TRIANGULATE: canAccess must still work for Admin by role
  it('should grant canAccess for Mora Temprana when user has Admin role', async () => {
    const component = await buildComponent(makeUser(['Admin']));
    const item = component.menuGroups
      .find(g => g.title === 'Cobranzas')?.items
      .find(i => i.route === '/app/collections/delinquency');
    expect(item).toBeDefined();
    expect(component.canAccess(item!)).toBeTrue();
  });
});
