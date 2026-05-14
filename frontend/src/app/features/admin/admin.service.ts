import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AdminUser {
    id: string;
    email: string;
    name: string;
    primaryRole: string;
    roles: string[];
    permissions: string[];
    status: string;
    lastActivityOn: string | null;
}

export interface AdminRoleSummary {
    name: string;
    description: string;
    usersCount: number;
}

export interface AdminRoleDetail {
    name: string;
    description: string;
    isCanonical: boolean;
    usersCount: number;
    permissions: string[];
}

export interface AdminPermissionEntry {
    value: string;
    description: string;
}

export interface CreateAdminUserRequest {
    email: string;
    password: string;
    roles: string[];
}

export interface CreateRoleRequest {
    name: string;
    description: string;
}

@Injectable({
    providedIn: 'root'
})
export class AdminService {
    private readonly http = inject(HttpClient);
    private readonly baseUrl = `${environment.apiUrl}/admin`;

    // ── Users ──────────────────────────────────────────────────────────

    getUsers(): Observable<AdminUser[]> {
        return this.http.get<AdminUser[]>(`${this.baseUrl}/users`);
    }

    createUser(request: CreateAdminUserRequest): Observable<AdminUser> {
        return this.http.post<AdminUser>(`${this.baseUrl}/users`, request);
    }

    deleteUser(userId: string): Observable<void> {
        return this.http.delete<void>(`${this.baseUrl}/users/${userId}`);
    }

    updateUserRoles(userId: string, roles: string[]): Observable<AdminUser> {
        return this.http.put<AdminUser>(`${this.baseUrl}/users/${userId}/roles`, { roles });
    }

    blockUser(userId: string): Observable<AdminUser> {
        return this.http.post<AdminUser>(`${this.baseUrl}/users/${userId}/block`, {});
    }

    unblockUser(userId: string): Observable<AdminUser> {
        return this.http.post<AdminUser>(`${this.baseUrl}/users/${userId}/unblock`, {});
    }

    resetPassword(userId: string, newPassword: string): Observable<void> {
        return this.http.post<void>(`${this.baseUrl}/users/${userId}/reset-password`, { newPassword });
    }

    getUserPermissions(userId: string): Observable<string[]> {
        return this.http.get<string[]>(`${this.baseUrl}/users/${userId}/permissions`);
    }

    setUserPermissions(userId: string, permissions: string[]): Observable<string[]> {
        return this.http.put<string[]>(`${this.baseUrl}/users/${userId}/permissions`, { permissions });
    }

    // ── Roles ──────────────────────────────────────────────────────────

    getRoles(): Observable<AdminRoleSummary[]> {
        return this.http.get<AdminRoleSummary[]>(`${this.baseUrl}/roles`);
    }

    createRole(request: CreateRoleRequest): Observable<AdminRoleDetail> {
        return this.http.post<AdminRoleDetail>(`${this.baseUrl}/roles`, request);
    }

    deleteRole(roleName: string): Observable<void> {
        return this.http.delete<void>(`${this.baseUrl}/roles/${roleName}`);
    }

    getRolePermissions(roleName: string): Observable<AdminRoleDetail> {
        return this.http.get<AdminRoleDetail>(`${this.baseUrl}/roles/${roleName}/permissions`);
    }

    setRolePermissions(roleName: string, permissions: string[]): Observable<AdminRoleDetail> {
        return this.http.put<AdminRoleDetail>(`${this.baseUrl}/roles/${roleName}/permissions`, { permissions });
    }

    // ── Permissions catalog ────────────────────────────────────────────

    getPermissions(): Observable<AdminPermissionEntry[]> {
        return this.http.get<AdminPermissionEntry[]>(`${this.baseUrl}/permissions`);
    }
}
