import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminPermissionEntry, AdminRoleDetail, AdminRoleSummary, AdminService } from './admin.service';
import { forkJoin } from 'rxjs';

@Component({
    selector: 'app-roles',
    standalone: true,
    imports: [CommonModule, FormsModule],
    template: `
    <div class="page-container">
      <div class="page-header">
        <div>
          <h1 class="page-title">Roles y Permisos</h1>
          <p class="text-muted mt-1">Gestiona roles y sus permisos granulares. Los roles canonicos no pueden eliminarse.</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline" type="button" (click)="load()" [disabled]="isLoading">
            <span class="material-symbols-rounded">refresh</span>
            Refrescar
          </button>
          <button class="btn btn-primary" type="button" (click)="openCreateModal()">
            <span class="material-symbols-rounded">add</span>
            Nuevo Rol
          </button>
        </div>
      </div>

      <div class="alert alert-error mb-4" *ngIf="errorMessage">{{ errorMessage }}</div>

      <div class="roles-layout">
        <!-- Lista de roles -->
        <div class="roles-list-card card p-0">
          <div class="card-header border-bottom p-3">
            <h3 class="card-title text-main mb-0">Roles ({{ roles.length }})</h3>
          </div>
          <div class="list-group list-group-flush">
            <button
              *ngFor="let role of roles"
              type="button"
              class="list-group-item list-group-item-action"
              [class.active]="selectedRole?.name === role.name"
              (click)="selectRole(role)"
            >
              <div class="d-flex justify-content-between align-items-center mb-1">
                <strong>{{ role.name }}</strong>
                <div class="d-flex align-items-center gap-1">
                  <span class="badge badge-subtle">{{ role.usersCount }} usuarios</span>
                  <span class="badge badge-canonical" *ngIf="isCanonical(role.name)">canónico</span>
                </div>
              </div>
              <p class="text-muted text-sm mb-0">{{ role.description }}</p>
            </button>
          </div>
        </div>

        <!-- Editor de permisos del rol seleccionado -->
        <div class="permissions-card card" *ngIf="selectedRoleDetail">
          <div class="editor-header d-flex justify-content-between align-items-center mb-3">
            <div>
              <h2 class="mb-1">{{ selectedRoleDetail.name }}</h2>
              <p class="text-muted mb-0 text-sm">{{ selectedRoleDetail.description }}</p>
            </div>
            <div class="d-flex gap-2">
              <button
                *ngIf="!selectedRoleDetail.isCanonical"
                class="btn btn-danger-outline"
                type="button"
                (click)="confirmDeleteRole()"
                [disabled]="selectedRoleDetail.usersCount > 0"
                [title]="selectedRoleDetail.usersCount > 0 ? 'Tiene usuarios asignados' : 'Eliminar rol'"
              >
                <span class="material-symbols-rounded">delete</span>
              </button>
              <button
                class="btn btn-primary"
                type="button"
                (click)="saveRolePermissions()"
                [disabled]="isSaving || selectedRoleDetail.isCanonical"
              >
                {{ isSaving ? 'Guardando...' : 'Guardar Permisos' }}
              </button>
            </div>
          </div>

          <div class="canonical-notice" *ngIf="selectedRoleDetail.isCanonical">
            <span class="material-symbols-rounded">info</span>
            Los permisos de roles canónicos se gestionan directamente por las políticas de autorización del sistema.
          </div>

          <div class="alert alert-error mb-3" *ngIf="saveError">{{ saveError }}</div>

          <div class="perms-grid" *ngIf="!selectedRoleDetail.isCanonical">
            <label
              *ngFor="let perm of allPermissions"
              class="perm-item"
              [class.checked]="editPermissions.includes(perm.value)"
            >
              <input
                type="checkbox"
                [checked]="editPermissions.includes(perm.value)"
                (change)="togglePermission(perm.value)"
              >
              <div class="perm-info">
                <span class="perm-value">{{ perm.value }}</span>
                <span class="perm-desc">{{ perm.description }}</span>
              </div>
            </label>
          </div>

          <!-- Vista lectura para roles canónicos -->
          <div class="perms-grid" *ngIf="selectedRoleDetail.isCanonical">
            <div
              *ngFor="let perm of allPermissions"
              class="perm-item readonly"
              [class.checked]="selectedRoleDetail.permissions.includes(perm.value)"
            >
              <span class="material-symbols-rounded perm-check">
                {{ selectedRoleDetail.permissions.includes(perm.value) ? 'check_circle' : 'radio_button_unchecked' }}
              </span>
              <div class="perm-info">
                <span class="perm-value">{{ perm.value }}</span>
                <span class="perm-desc">{{ perm.description }}</span>
              </div>
            </div>
          </div>
        </div>

        <div class="empty-state card" *ngIf="!selectedRoleDetail && !isLoading">
          <span class="material-symbols-rounded">manage_accounts</span>
          <p>Selecciona un rol para ver y editar sus permisos.</p>
        </div>
      </div>

      <!-- Modal crear rol -->
      <div class="modal-overlay" [class.show]="isCreateModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Crear Nuevo Rol</h3>
            <button class="icon-btn" type="button" (click)="closeCreateModal()">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body">
            <div class="input-group">
              <label>Nombre del rol</label>
              <input type="text" class="form-control" [(ngModel)]="newRoleName" placeholder="Ej: Compliance, ReadOnly">
            </div>
            <div class="input-group">
              <label>Descripcion</label>
              <input type="text" class="form-control" [(ngModel)]="newRoleDescription" placeholder="Describe para qué sirve este rol">
            </div>
            <div class="alert alert-error mt-2" *ngIf="createError">{{ createError }}</div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" type="button" (click)="closeCreateModal()">Cancelar</button>
            <button class="btn btn-primary" type="button" [disabled]="!newRoleName.trim() || isSaving" (click)="createRole()">
              {{ isSaving ? 'Creando...' : 'Crear Rol' }}
            </button>
          </div>
        </div>
      </div>

      <!-- Modal confirmar eliminar -->
      <div class="modal-overlay" [class.show]="isDeleteModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Eliminar Rol</h3>
            <button class="icon-btn" type="button" (click)="closeDeleteModal()">
              <span class="material-symbols-rounded">close</span>
            </button>
          </div>
          <div class="modal-body">
            <p>¿Confirmas que deseas eliminar el rol <strong>{{ selectedRoleDetail?.name }}</strong>?</p>
            <p class="text-muted text-sm">Esta acción no puede deshacerse.</p>
            <div class="alert alert-error mt-2" *ngIf="deleteError">{{ deleteError }}</div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" type="button" (click)="closeDeleteModal()">Cancelar</button>
            <button class="btn btn-danger" type="button" [disabled]="isSaving" (click)="deleteRole()">
              {{ isSaving ? 'Eliminando...' : 'Eliminar' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
    styles: [`
    .page-container { padding-bottom: 2rem; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
    .page-title { font-size: 1.5rem; color: var(--primary); margin: 0; }
    .header-actions { display: flex; gap: 1rem; }
    .d-flex { display: flex; }
    .gap-1 { gap: 0.35rem; }
    .gap-2 { gap: 0.75rem; }
    .align-items-center { align-items: center; }
    .justify-content-between { justify-content: space-between; }
    .mb-0 { margin-bottom: 0; }
    .mb-1 { margin-bottom: 0.25rem; }
    .mb-3 { margin-bottom: 1rem; }
    .mb-4 { margin-bottom: 1.5rem; }
    .mt-2 { margin-top: 0.5rem; }
    .text-sm { font-size: 0.8rem; }
    .alert { padding: 0.9rem 1rem; border-radius: var(--radius-md); }
    .alert-error { background-color: #fdecec; border: 1px solid #f6c7c7; color: #b42318; }
    .roles-layout { display: grid; grid-template-columns: 340px 1fr; gap: 1.5rem; align-items: start; }
    .roles-list-card { min-width: 0; }
    .card-header { background: transparent; }
    .list-group { display: flex; flex-direction: column; }
    .list-group-item {
      padding: 1rem 1.25rem;
      background: var(--bg-paper);
      border: 0;
      border-bottom: 1px solid var(--border-color);
      text-align: left;
      cursor: pointer;
      transition: background-color 0.15s;
    }
    .list-group-item:last-child { border-bottom: 0; }
    .list-group-item:hover { background: var(--bg-main); }
    .list-group-item.active { background: var(--primary-light); border-left: 4px solid var(--primary); padding-left: calc(1.25rem - 4px); }
    .badge-subtle { background: #f1f5f9; color: #64748b; padding: 0.2rem 0.5rem; border-radius: 12px; font-size: 0.7rem; font-weight: 500; }
    .badge-canonical { background: #e0e7ff; color: #4338ca; padding: 0.2rem 0.5rem; border-radius: 12px; font-size: 0.7rem; font-weight: 500; }
    .permissions-card { min-width: 0; }
    .canonical-notice {
      display: flex; align-items: center; gap: 0.5rem;
      background: #f0f9ff; border: 1px solid #bae6fd; color: #0369a1;
      padding: 0.75rem 1rem; border-radius: var(--radius-md); margin-bottom: 1rem;
      font-size: 0.875rem;
    }
    .perms-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 0.5rem; }
    .perm-item {
      display: flex; align-items: flex-start; gap: 0.75rem;
      padding: 0.75rem; border-radius: var(--radius-md);
      border: 1px solid var(--border-color); cursor: pointer;
      transition: border-color 0.15s, background 0.15s;
    }
    .perm-item.checked { border-color: var(--primary); background: var(--primary-light); }
    .perm-item.readonly { cursor: default; }
    .perm-item input[type="checkbox"] { margin-top: 2px; flex-shrink: 0; }
    .perm-check { font-size: 1.1rem; flex-shrink: 0; margin-top: 2px; }
    .perm-item.checked .perm-check { color: var(--primary); }
    .perm-info { display: flex; flex-direction: column; gap: 0.15rem; }
    .perm-value { font-size: 0.8rem; font-weight: 600; font-family: monospace; color: var(--text-main); }
    .perm-desc { font-size: 0.75rem; color: var(--text-muted); }
    .empty-state { display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 4rem 2rem; color: var(--text-muted); gap: 1rem; }
    .empty-state span { font-size: 3rem; }
    .btn-danger-outline { background: transparent; border: 1px solid var(--danger, #dc2626); color: var(--danger, #dc2626); padding: 0.4rem 0.75rem; border-radius: var(--radius-md); cursor: pointer; display: inline-flex; align-items: center; gap: 0.25rem; }
    .btn-danger-outline:hover:not(:disabled) { background: #fee2e2; }
    .btn-danger-outline:disabled { opacity: 0.4; cursor: not-allowed; }
    .btn-danger { background: #dc2626; color: #fff; border: none; padding: 0.5rem 1.25rem; border-radius: var(--radius-md); cursor: pointer; font-weight: 600; }
    .btn-danger:hover:not(:disabled) { background: #b91c1c; }
    .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.4); display: none; align-items: center; justify-content: center; z-index: 1000; }
    .modal-overlay.show { display: flex; }
    .modal-card { width: 100%; max-width: 480px; padding: 0; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; padding: 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-header h3 { margin: 0; }
    .modal-body { padding: 1.5rem; }
    .modal-footer { display: flex; justify-content: flex-end; gap: 0.75rem; padding: 1rem 1.5rem; border-top: 1px solid var(--border-color); background: var(--bg-main); }
    .input-group { margin-bottom: 1rem; }
    .input-group label { display: block; font-weight: 600; margin-bottom: 0.4rem; }
    .icon-btn { background: none; border: none; cursor: pointer; color: var(--text-muted); padding: 0.25rem; border-radius: 4px; }
    .icon-btn:hover { background: var(--bg-main); color: var(--primary); }
  `]
})
export class RolesComponent implements OnInit {
    private readonly adminService = inject(AdminService);

    roles: AdminRoleSummary[] = [];
    selectedRole: AdminRoleSummary | null = null;
    selectedRoleDetail: AdminRoleDetail | null = null;
    allPermissions: AdminPermissionEntry[] = [];
    editPermissions: string[] = [];

    isLoading = false;
    isSaving = false;
    errorMessage = '';
    saveError = '';
    createError = '';
    deleteError = '';

    isCreateModalOpen = false;
    isDeleteModalOpen = false;
    newRoleName = '';
    newRoleDescription = '';

    readonly canonicalRoles = ['Admin', 'Operator', 'Auditor'];

    ngOnInit(): void {
        this.load();
    }

    load(): void {
        this.isLoading = true;
        this.errorMessage = '';
        // Cargamos roles y catálogo de permisos en paralelo
        const roles$ = this.adminService.getRoles();
        const perms$ = this.adminService.getPermissions();
        let rolesLoaded = false, permsLoaded = false;

        const tryFinish = () => {
            if (rolesLoaded && permsLoaded) {
                this.isLoading = false;
                if (this.roles.length > 0 && !this.selectedRole) {
                    this.selectRole(this.roles[0]);
                }
            }
        };

        roles$.subscribe({
            next: (roles) => { this.roles = roles; rolesLoaded = true; tryFinish(); },
            error: () => { this.errorMessage = 'No fue posible cargar los roles.'; this.isLoading = false; }
        });

        perms$.subscribe({
            next: (perms) => { this.allPermissions = perms; permsLoaded = true; tryFinish(); },
            error: () => { this.errorMessage = 'No fue posible cargar el catálogo de permisos.'; this.isLoading = false; }
        });
    }

    selectRole(role: AdminRoleSummary): void {
        this.selectedRole = role;
        this.selectedRoleDetail = null;
        this.saveError = '';
        this.adminService.getRolePermissions(role.name).subscribe({
            next: (detail) => {
                this.selectedRoleDetail = detail;
                this.editPermissions = [...detail.permissions];
            },
            error: () => { this.errorMessage = `No fue posible cargar permisos de '${role.name}'.`; }
        });
    }

    togglePermission(value: string): void {
        this.editPermissions = this.editPermissions.includes(value)
            ? this.editPermissions.filter(p => p !== value)
            : [...this.editPermissions, value];
    }

    saveRolePermissions(): void {
        if (!this.selectedRole || !this.selectedRoleDetail || this.selectedRoleDetail.isCanonical) return;
        this.isSaving = true;
        this.saveError = '';
        this.adminService.setRolePermissions(this.selectedRole.name, this.editPermissions).subscribe({
            next: (detail) => {
                this.selectedRoleDetail = detail;
                this.editPermissions = [...detail.permissions];
                this.isSaving = false;
            },
            error: (err) => {
                this.saveError = err?.error?.message ?? 'No fue posible guardar los permisos.';
                this.isSaving = false;
            }
        });
    }

    isCanonical(name: string): boolean {
        return this.canonicalRoles.includes(name);
    }

    openCreateModal(): void {
        this.newRoleName = '';
        this.newRoleDescription = '';
        this.createError = '';
        this.isCreateModalOpen = true;
    }

    closeCreateModal(): void {
        this.isCreateModalOpen = false;
    }

    createRole(): void {
        if (!this.newRoleName.trim()) return;
        this.isSaving = true;
        this.createError = '';
        this.adminService.createRole({ name: this.newRoleName.trim(), description: this.newRoleDescription.trim() }).subscribe({
            next: (detail) => {
                this.roles = [...this.roles, { name: detail.name, description: detail.description, usersCount: 0 }]
                    .sort((a, b) => a.name.localeCompare(b.name));
                this.isSaving = false;
                this.closeCreateModal();
                const newRole = this.roles.find(r => r.name === detail.name);
                if (newRole) this.selectRole(newRole);
            },
            error: (err) => {
                this.createError = err?.error?.message ?? 'No fue posible crear el rol.';
                this.isSaving = false;
            }
        });
    }

    confirmDeleteRole(): void {
        this.deleteError = '';
        this.isDeleteModalOpen = true;
    }

    closeDeleteModal(): void {
        this.isDeleteModalOpen = false;
    }

    deleteRole(): void {
        if (!this.selectedRole) return;
        this.isSaving = true;
        this.deleteError = '';
        this.adminService.deleteRole(this.selectedRole.name).subscribe({
            next: () => {
                this.roles = this.roles.filter(r => r.name !== this.selectedRole!.name);
                this.selectedRole = null;
                this.selectedRoleDetail = null;
                this.editPermissions = [];
                this.isSaving = false;
                this.closeDeleteModal();
                if (this.roles.length > 0) this.selectRole(this.roles[0]);
            },
            error: (err) => {
                this.deleteError = err?.error?.message ?? 'No fue posible eliminar el rol.';
                this.isSaving = false;
            }
        });
    }
}
