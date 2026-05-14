import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { AdminPermissionEntry, AdminService, AdminUser } from './admin.service';

@Component({
    selector: 'app-users',
    standalone: true,
    imports: [CommonModule, FormsModule, ReactiveFormsModule],
    template: `
    <div class="page-container">
      <div class="page-header">
        <div>
          <h1 class="page-title">Gestion de Usuarios</h1>
          <p class="text-muted mt-1">Usuarios de Identity con roles, permisos individuales y controles de acceso.</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-outline" type="button" (click)="loadUsers()" [disabled]="isLoading">
            <span class="material-symbols-rounded">refresh</span>
            Refrescar
          </button>
          <button class="btn btn-primary" type="button" (click)="openCreateModal()">
            <span class="material-symbols-rounded">person_add</span>
            Nuevo Usuario
          </button>
        </div>
      </div>

      <div class="filters-card card mb-4">
        <div class="search-box">
          <span class="material-symbols-rounded search-icon">search</span>
          <input type="text" class="search-input" placeholder="Buscar por correo o rol..." [(ngModel)]="searchTerm">
        </div>
      </div>

      <div class="alert alert-error mb-4" *ngIf="errorMessage">{{ errorMessage }}</div>

      <div class="card p-0">
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>USUARIO</th>
                <th>ROL PRIMARIO</th>
                <th>ROLES</th>
                <th>ESTADO</th>
                <th>ULTIMA ACTIVIDAD</th>
                <th class="text-right">ACCIONES</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let user of filteredUsers">
                <td>
                  <div class="user-block">
                    <div class="user-avatar">{{ user.name.charAt(0) | uppercase }}</div>
                    <div class="user-details">
                      <span class="user-name">{{ user.name }}</span>
                      <span class="user-email text-muted">{{ user.email }}</span>
                    </div>
                  </div>
                </td>
                <td><span class="role-badge role-primary">{{ user.primaryRole }}</span></td>
                <td>
                  <div class="roles-cell">
                    <span class="role-badge" *ngFor="let role of user.roles" [ngClass]="getRoleClass(role)">{{ role }}</span>
                  </div>
                </td>
                <td>
                  <span class="status-badge" [ngClass]="getStatusClass(user.status)">
                    {{ getStatusLabel(user.status) }}
                  </span>
                </td>
                <td class="text-muted">{{ user.lastActivityOn ? (user.lastActivityOn | date:'medium') : 'Sin registro' }}</td>
                <td class="text-right">
                  <div class="action-buttons">
                    <button class="icon-btn" type="button" title="Editar roles" (click)="openRolesModal(user)">
                      <span class="material-symbols-rounded">manage_accounts</span>
                    </button>
                    <button class="icon-btn" type="button" title="Permisos individuales" (click)="openPermissionsModal(user)">
                      <span class="material-symbols-rounded">shield</span>
                    </button>
                    <button class="icon-btn" type="button" title="Resetear contraseña" (click)="openResetPasswordModal(user)">
                      <span class="material-symbols-rounded">key</span>
                    </button>
                    <button
                      *ngIf="user.status !== 'Blocked'"
                      class="icon-btn warn-btn"
                      type="button"
                      title="Bloquear usuario"
                      (click)="blockUser(user)"
                    >
                      <span class="material-symbols-rounded">block</span>
                    </button>
                    <button
                      *ngIf="user.status === 'Blocked'"
                      class="icon-btn ok-btn"
                      type="button"
                      title="Desbloquear usuario"
                      (click)="unblockUser(user)"
                    >
                      <span class="material-symbols-rounded">lock_open</span>
                    </button>
                    <button class="icon-btn danger-btn" type="button" title="Eliminar usuario" (click)="confirmDeleteUser(user)">
                      <span class="material-symbols-rounded">delete</span>
                    </button>
                  </div>
                </td>
              </tr>
              <tr *ngIf="!isLoading && filteredUsers.length === 0">
                <td colspan="6" class="text-center py-5 text-muted">No se encontraron usuarios.</td>
              </tr>
              <tr *ngIf="isLoading">
                <td colspan="6" class="text-center py-5 text-muted">Cargando usuarios...</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Modal crear usuario -->
      <div class="modal-overlay" [class.show]="isCreateModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Crear Usuario</h3>
            <button class="icon-btn" type="button" (click)="closeCreateModal()"><span class="material-symbols-rounded">close</span></button>
          </div>
          <form class="modal-body" [formGroup]="createForm" (ngSubmit)="createUser()">
            <div class="input-group">
              <label>Correo Electronico</label>
              <input type="email" class="form-control" formControlName="email" placeholder="usuario@demo.com">
            </div>
            <div class="input-group">
              <label>Contrasena</label>
              <input type="password" class="form-control" formControlName="password" placeholder="Minimo 8 caracteres">
            </div>
            <div class="input-group">
              <label>Roles</label>
              <div class="role-selector">
                <label class="role-option" *ngFor="let role of availableRoles">
                  <input type="checkbox" [checked]="createRoles.includes(role)" (change)="toggleCreateRole(role)">
                  <span>{{ role }}</span>
                </label>
              </div>
            </div>
            <div class="alert alert-error" *ngIf="createError">{{ createError }}</div>
            <div class="modal-footer">
              <button class="btn btn-outline" type="button" (click)="closeCreateModal()">Cancelar</button>
              <button class="btn btn-primary" type="submit" [disabled]="createForm.invalid || createRoles.length === 0 || isSaving">
                {{ isSaving ? 'Guardando...' : 'Guardar Usuario' }}
              </button>
            </div>
          </form>
        </div>
      </div>

      <!-- Modal editar roles -->
      <div class="modal-overlay" [class.show]="isRolesModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Asignar Roles</h3>
            <button class="icon-btn" type="button" (click)="closeRolesModal()"><span class="material-symbols-rounded">close</span></button>
          </div>
          <div class="modal-body" *ngIf="selectedUser">
            <p class="text-muted mb-3">{{ selectedUser.email }}</p>
            <div class="role-selector">
              <label class="role-option" *ngFor="let role of availableRoles">
                <input type="checkbox" [checked]="editRoles.includes(role)" (change)="toggleEditRole(role)">
                <span>{{ role }}</span>
              </label>
            </div>
            <div class="alert alert-error mt-3" *ngIf="editError">{{ editError }}</div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" type="button" (click)="closeRolesModal()">Cancelar</button>
            <button class="btn btn-primary" type="button" [disabled]="editRoles.length === 0 || isSaving" (click)="saveRoles()">
              {{ isSaving ? 'Guardando...' : 'Guardar Roles' }}
            </button>
          </div>
        </div>
      </div>

      <!-- Modal permisos individuales -->
      <div class="modal-overlay" [class.show]="isPermissionsModalOpen">
        <div class="modal-card modal-card-lg card">
          <div class="modal-header">
            <div>
              <h3>Permisos Individuales</h3>
              <p class="text-muted text-sm mb-0" *ngIf="selectedUser">{{ selectedUser.email }}</p>
            </div>
            <button class="icon-btn" type="button" (click)="closePermissionsModal()"><span class="material-symbols-rounded">close</span></button>
          </div>
          <div class="modal-body">
            <p class="text-muted text-sm mb-3">
              Permisos asignados directamente al usuario (adicionales a los del rol).
            </p>
            <div class="perms-grid-modal" *ngIf="allPermissions.length > 0">
              <label
                *ngFor="let perm of allPermissions"
                class="perm-item-sm"
                [class.checked]="editPermissions.includes(perm.value)"
              >
                <input type="checkbox" [checked]="editPermissions.includes(perm.value)" (change)="toggleUserPermission(perm.value)">
                <div class="perm-info">
                  <span class="perm-value">{{ perm.value }}</span>
                  <span class="perm-desc">{{ perm.description }}</span>
                </div>
              </label>
            </div>
            <div class="alert alert-error mt-3" *ngIf="permissionsError">{{ permissionsError }}</div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" type="button" (click)="closePermissionsModal()">Cancelar</button>
            <button class="btn btn-primary" type="button" [disabled]="isSaving" (click)="saveUserPermissions()">
              {{ isSaving ? 'Guardando...' : 'Guardar Permisos' }}
            </button>
          </div>
        </div>
      </div>

      <!-- Modal reset password -->
      <div class="modal-overlay" [class.show]="isResetPasswordModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Resetear Contrasena</h3>
            <button class="icon-btn" type="button" (click)="closeResetPasswordModal()"><span class="material-symbols-rounded">close</span></button>
          </div>
          <div class="modal-body" *ngIf="selectedUser">
            <p class="text-muted mb-3">{{ selectedUser.email }}</p>
            <div class="input-group">
              <label>Nueva Contrasena</label>
              <input type="password" class="form-control" [(ngModel)]="newPassword" placeholder="Minimo 8 caracteres">
            </div>
            <div class="alert alert-error mt-2" *ngIf="resetPasswordError">{{ resetPasswordError }}</div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" type="button" (click)="closeResetPasswordModal()">Cancelar</button>
            <button class="btn btn-primary" type="button" [disabled]="newPassword.length < 8 || isSaving" (click)="resetPassword()">
              {{ isSaving ? 'Aplicando...' : 'Aplicar Cambio' }}
            </button>
          </div>
        </div>
      </div>

      <!-- Modal eliminar usuario -->
      <div class="modal-overlay" [class.show]="isDeleteModalOpen">
        <div class="modal-card card">
          <div class="modal-header">
            <h3>Eliminar Usuario</h3>
            <button class="icon-btn" type="button" (click)="closeDeleteModal()"><span class="material-symbols-rounded">close</span></button>
          </div>
          <div class="modal-body" *ngIf="selectedUser">
            <p>¿Confirmas que deseas eliminar a <strong>{{ selectedUser.email }}</strong>?</p>
            <p class="text-muted text-sm">Esta acción no puede deshacerse.</p>
            <div class="alert alert-error mt-2" *ngIf="deleteError">{{ deleteError }}</div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-outline" type="button" (click)="closeDeleteModal()">Cancelar</button>
            <button class="btn btn-danger" type="button" [disabled]="isSaving" (click)="deleteUser()">
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
    .filters-card { padding: 1rem 1.5rem; }
    .search-box { display: flex; align-items: center; background-color: var(--bg-main); border-radius: var(--radius-md); padding: 0.5rem 1rem; width: 100%; max-width: 420px; }
    .search-input { border: none; background: transparent; outline: none; width: 100%; margin-left: 0.5rem; font-family: inherit; }
    .search-icon { color: var(--text-muted); }
    .mb-3 { margin-bottom: 1rem; }
    .mb-4 { margin-bottom: 1.5rem; }
    .mt-2 { margin-top: 0.5rem; }
    .mt-3 { margin-top: 1rem; }
    .text-sm { font-size: 0.8rem; }
    .alert { padding: 0.9rem 1rem; border-radius: var(--radius-md); }
    .alert-error { background-color: #fdecec; border: 1px solid #f6c7c7; color: #b42318; }
    .table-responsive { overflow-x: auto; }
    .table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    .table th, .table td { padding: 1rem 1.5rem; text-align: left; border-bottom: 1px solid var(--border-color); vertical-align: middle; }
    .table th { color: var(--text-muted); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; }
    .user-block { display: flex; align-items: center; gap: 1rem; }
    .user-avatar { width: 40px; height: 40px; border-radius: 50%; background-color: var(--primary-light); color: var(--primary-dark); display: flex; align-items: center; justify-content: center; font-weight: 600; }
    .user-details { display: flex; flex-direction: column; }
    .user-name { font-weight: 600; color: var(--text-main); }
    .user-email { font-size: 0.75rem; }
    .roles-cell { display: flex; flex-wrap: wrap; gap: 0.35rem; }
    .role-badge { padding: 0.25rem 0.6rem; border-radius: var(--radius-sm); font-size: 0.75rem; font-weight: 600; }
    .role-primary { background: #e0e7ff; color: #4338ca; }
    .role-admin { background: #e0e7ff; color: #4338ca; }
    .role-operator { background: #fef3c7; color: #b45309; }
    .role-auditor { background: #ecfeff; color: #155e75; }
    .status-badge { padding: 0.25rem 0.6rem; border-radius: 1rem; font-size: 0.75rem; font-weight: 600; display: inline-flex; align-items: center; gap: 0.25rem; }
    .status-active { background: #ecfdf5; color: #047857; }
    .status-inactive { background: #f3f4f6; color: #4b5563; }
    .status-blocked { background: #fef2f2; color: #b91c1c; }
    .action-buttons { display: flex; align-items: center; justify-content: flex-end; gap: 0.25rem; }
    .icon-btn { background: none; border: none; cursor: pointer; color: var(--text-muted); padding: 0.3rem; border-radius: 4px; display: inline-flex; align-items: center; }
    .icon-btn:hover { background: var(--bg-main); color: var(--primary); }
    .warn-btn:hover { color: #b45309; background: #fef3c7; }
    .ok-btn:hover { color: #047857; background: #ecfdf5; }
    .danger-btn:hover { color: #b91c1c; background: #fef2f2; }
    .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.4); display: none; align-items: center; justify-content: center; z-index: 1000; }
    .modal-overlay.show { display: flex; }
    .modal-card { width: 100%; max-width: 520px; padding: 0; }
    .modal-card-lg { max-width: 740px; }
    .modal-header { display: flex; justify-content: space-between; align-items: flex-start; padding: 1.5rem; border-bottom: 1px solid var(--border-color); }
    .modal-header h3 { margin: 0; }
    .modal-body { padding: 1.5rem; max-height: 60vh; overflow-y: auto; }
    .modal-footer { display: flex; justify-content: flex-end; gap: 0.75rem; padding: 1rem 1.5rem; border-top: 1px solid var(--border-color); background: var(--bg-main); }
    .input-group { margin-bottom: 1rem; }
    .input-group label { display: block; font-weight: 600; margin-bottom: 0.4rem; }
    .role-selector { display: flex; flex-wrap: wrap; gap: 0.75rem; }
    .role-option { display: inline-flex; align-items: center; gap: 0.35rem; background: var(--bg-main); padding: 0.5rem 0.75rem; border-radius: var(--radius-md); cursor: pointer; }
    .perms-grid-modal { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 0.4rem; }
    .perm-item-sm { display: flex; align-items: flex-start; gap: 0.5rem; padding: 0.5rem 0.6rem; border-radius: var(--radius-sm); border: 1px solid var(--border-color); cursor: pointer; transition: border-color 0.15s, background 0.15s; }
    .perm-item-sm.checked { border-color: var(--primary); background: var(--primary-light); }
    .perm-item-sm input { margin-top: 2px; flex-shrink: 0; }
    .perm-info { display: flex; flex-direction: column; gap: 0.1rem; }
    .perm-value { font-size: 0.75rem; font-weight: 600; font-family: monospace; color: var(--text-main); }
    .perm-desc { font-size: 0.7rem; color: var(--text-muted); }
    .btn-danger { background: #dc2626; color: #fff; border: none; padding: 0.5rem 1.25rem; border-radius: var(--radius-md); cursor: pointer; font-weight: 600; }
    .btn-danger:hover:not(:disabled) { background: #b91c1c; }
    .text-right { text-align: right; }
    .text-center { text-align: center; }
    .py-5 { padding-top: 2rem; padding-bottom: 2rem; }
  `]
})
export class UsersComponent implements OnInit {
    private readonly adminService = inject(AdminService);
    private readonly fb = inject(FormBuilder);

    availableRoles = ['Admin', 'Operator', 'Auditor'];
    allPermissions: AdminPermissionEntry[] = [];

    users: AdminUser[] = [];
    searchTerm = '';
    isLoading = false;
    isSaving = false;
    errorMessage = '';
    createError = '';
    editError = '';
    permissionsError = '';
    resetPasswordError = '';
    deleteError = '';

    isCreateModalOpen = false;
    isRolesModalOpen = false;
    isPermissionsModalOpen = false;
    isResetPasswordModalOpen = false;
    isDeleteModalOpen = false;

    selectedUser: AdminUser | null = null;
    createRoles: string[] = ['Operator'];
    editRoles: string[] = [];
    editPermissions: string[] = [];
    newPassword = '';

    createForm: FormGroup = this.fb.group({
        email: ['', [Validators.required, Validators.email]],
        password: ['Admin1234!', [Validators.required, Validators.minLength(8)]]
    });

    ngOnInit(): void {
        this.loadUsers();
        this.adminService.getPermissions().subscribe({
            next: (perms) => { this.allPermissions = perms; },
            error: () => { /* non-critical */ }
        });
    }

    get filteredUsers(): AdminUser[] {
        const term = this.searchTerm.trim().toLowerCase();
        if (!term) return this.users;
        return this.users.filter(u =>
            u.email.toLowerCase().includes(term) ||
            u.roles.some(r => r.toLowerCase().includes(term))
        );
    }

    loadUsers(): void {
        this.isLoading = true;
        this.errorMessage = '';
        this.adminService.getUsers().subscribe({
            next: (users) => { this.users = users; this.isLoading = false; },
            error: () => { this.errorMessage = 'No fue posible cargar usuarios desde el backend.'; this.isLoading = false; }
        });
    }

    // ── Create user ──────────────────────────────────────────────────

    openCreateModal(): void {
        this.createError = '';
        this.createRoles = ['Operator'];
        this.createForm.reset({ email: '', password: 'Admin1234!' });
        this.isCreateModalOpen = true;
    }

    closeCreateModal(): void { this.isCreateModalOpen = false; }

    toggleCreateRole(role: string): void { this.createRoles = this.toggleRole(role, this.createRoles); }

    createUser(): void {
        if (this.createForm.invalid || this.createRoles.length === 0) return;
        this.isSaving = true;
        this.createError = '';
        const { email, password } = this.createForm.getRawValue();
        this.adminService.createUser({ email, password, roles: this.createRoles }).subscribe({
            next: (user) => {
                this.users = [...this.users, user].sort((a, b) => a.email.localeCompare(b.email));
                this.isSaving = false;
                this.closeCreateModal();
            },
            error: (err) => { this.createError = err?.error?.message ?? 'No fue posible crear el usuario.'; this.isSaving = false; }
        });
    }

    // ── Roles ────────────────────────────────────────────────────────

    openRolesModal(user: AdminUser): void {
        this.selectedUser = user;
        this.editRoles = [...user.roles];
        this.editError = '';
        this.isRolesModalOpen = true;
    }

    closeRolesModal(): void { this.isRolesModalOpen = false; this.selectedUser = null; }

    toggleEditRole(role: string): void { this.editRoles = this.toggleRole(role, this.editRoles); }

    saveRoles(): void {
        if (!this.selectedUser || this.editRoles.length === 0) return;
        this.isSaving = true;
        this.editError = '';
        this.adminService.updateUserRoles(this.selectedUser.id, this.editRoles).subscribe({
            next: (updated) => {
                this.users = this.users.map(u => u.id === updated.id ? updated : u);
                this.isSaving = false;
                this.closeRolesModal();
            },
            error: (err) => { this.editError = err?.error?.message ?? 'No fue posible actualizar los roles.'; this.isSaving = false; }
        });
    }

    // ── Permissions ──────────────────────────────────────────────────

    openPermissionsModal(user: AdminUser): void {
        this.selectedUser = user;
        this.permissionsError = '';
        this.isSaving = false;
        this.adminService.getUserPermissions(user.id).subscribe({
            next: (perms) => { this.editPermissions = [...perms]; this.isPermissionsModalOpen = true; },
            error: () => { this.errorMessage = 'No fue posible cargar los permisos del usuario.'; }
        });
    }

    closePermissionsModal(): void { this.isPermissionsModalOpen = false; this.selectedUser = null; }

    toggleUserPermission(value: string): void {
        this.editPermissions = this.editPermissions.includes(value)
            ? this.editPermissions.filter(p => p !== value)
            : [...this.editPermissions, value];
    }

    saveUserPermissions(): void {
        if (!this.selectedUser) return;
        this.isSaving = true;
        this.permissionsError = '';
        this.adminService.setUserPermissions(this.selectedUser.id, this.editPermissions).subscribe({
            next: () => {
                // Actualizar permisos en la lista local
                this.users = this.users.map(u =>
                    u.id === this.selectedUser!.id ? { ...u, permissions: [...this.editPermissions] } : u
                );
                this.isSaving = false;
                this.closePermissionsModal();
            },
            error: (err) => { this.permissionsError = err?.error?.message ?? 'No fue posible guardar los permisos.'; this.isSaving = false; }
        });
    }

    // ── Block / Unblock ──────────────────────────────────────────────

    blockUser(user: AdminUser): void {
        this.adminService.blockUser(user.id).subscribe({
            next: (updated) => { this.users = this.users.map(u => u.id === updated.id ? updated : u); },
            error: () => { this.errorMessage = 'No fue posible bloquear el usuario.'; }
        });
    }

    unblockUser(user: AdminUser): void {
        this.adminService.unblockUser(user.id).subscribe({
            next: (updated) => { this.users = this.users.map(u => u.id === updated.id ? updated : u); },
            error: () => { this.errorMessage = 'No fue posible desbloquear el usuario.'; }
        });
    }

    // ── Reset password ───────────────────────────────────────────────

    openResetPasswordModal(user: AdminUser): void {
        this.selectedUser = user;
        this.newPassword = '';
        this.resetPasswordError = '';
        this.isResetPasswordModalOpen = true;
    }

    closeResetPasswordModal(): void { this.isResetPasswordModalOpen = false; this.selectedUser = null; }

    resetPassword(): void {
        if (!this.selectedUser || this.newPassword.length < 8) return;
        this.isSaving = true;
        this.resetPasswordError = '';
        this.adminService.resetPassword(this.selectedUser.id, this.newPassword).subscribe({
            next: () => { this.isSaving = false; this.closeResetPasswordModal(); },
            error: (err) => { this.resetPasswordError = err?.error?.message ?? 'No fue posible resetear la contraseña.'; this.isSaving = false; }
        });
    }

    // ── Delete user ──────────────────────────────────────────────────

    confirmDeleteUser(user: AdminUser): void {
        this.selectedUser = user;
        this.deleteError = '';
        this.isDeleteModalOpen = true;
    }

    closeDeleteModal(): void { this.isDeleteModalOpen = false; this.selectedUser = null; }

    deleteUser(): void {
        if (!this.selectedUser) return;
        this.isSaving = true;
        this.deleteError = '';
        this.adminService.deleteUser(this.selectedUser.id).subscribe({
            next: () => {
                this.users = this.users.filter(u => u.id !== this.selectedUser!.id);
                this.isSaving = false;
                this.closeDeleteModal();
            },
            error: (err) => { this.deleteError = err?.error?.message ?? 'No fue posible eliminar el usuario.'; this.isSaving = false; }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    getRoleClass(role: string): string {
        switch (role) {
            case 'Admin': return 'role-admin';
            case 'Operator': return 'role-operator';
            case 'Auditor': return 'role-auditor';
            default: return 'role-primary';
        }
    }

    getStatusClass(status: string): string {
        switch (status) {
            case 'Active': return 'status-active';
            case 'Blocked': return 'status-blocked';
            default: return 'status-inactive';
        }
    }

    getStatusLabel(status: string): string {
        switch (status) {
            case 'Active': return 'Activo';
            case 'Blocked': return 'Bloqueado';
            default: return 'Inactivo';
        }
    }

    private toggleRole(role: string, current: string[]): string[] {
        return current.includes(role)
            ? current.filter(item => item !== role)
            : [...current, role];
    }
}
