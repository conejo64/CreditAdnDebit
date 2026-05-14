import { Routes } from '@angular/router';
import { LoginComponent } from './features/auth/login.component';
import { ForgotPasswordComponent } from './features/auth/forgot-password.component';
import { MainLayoutComponent } from './layout/main-layout.component';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { UsersComponent } from './features/admin/users.component';
import { RolesComponent } from './features/admin/roles.component';
import { CustomerListComponent } from './features/issuer/customers/customer-list.component';
import { CustomerDetailComponent } from './features/issuer/customers/customer-detail.component';
import { CardListComponent } from './features/issuer/cards/card-list.component';
import { CardDetailComponent } from './features/issuer/cards/card-detail.component';
import { AccountListComponent } from './features/issuer/account-list.component';
import { LedgerListComponent } from './features/finance/ledger-list.component';
import { BillingStatementComponent } from './features/finance/billing-statement.component';
import { AuditListComponent } from './features/switch/audit-list.component';
import { SettlementListComponent } from './features/finance/settlement-list.component';
import { DisputeListComponent } from './features/security/dispute-list.component';
import { AntifraudListComponent } from './features/security/antifraud-list.component';
import { VaultComponent } from './features/security/vault.component';
import { EcommerceMonitorComponent } from './features/security/ecommerce-monitor.component';
import { InstallmentListComponent } from './features/finance/installment-list.component';
import { SimulatorComponent } from './features/switch/simulator.component';
import { CatalogsComponent } from './features/switch/catalogs.component';
import { RoutingComponent } from './features/switch/routing.component';
import { UnderConstructionComponent } from './shared/components/under-construction.component';
import { authGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';
import { AccountingListComponent } from './features/finance/accounting-list.component';
import { AnalyticsDashboardComponent } from './features/finance/analytics-dashboard.component';
import { CreditLimitListComponent } from './features/finance/credit-limit-list.component';
import { LoyaltyListComponent } from './features/ecosystem/loyalty-list.component';
import { WalletsListComponent } from './features/ecosystem/wallets-list.component';
import { NotificationsListComponent } from './features/ecosystem/notifications-list.component';
import { OpenBankingListComponent } from './features/ecosystem/open-banking-list.component';

export const routes: Routes = [
    { path: '', redirectTo: 'auth/login', pathMatch: 'full' },
    { path: 'auth/login', component: LoginComponent },
    { path: 'auth/forgot-password', component: ForgotPasswordComponent },
    {
        path: 'app',
        component: MainLayoutComponent,
        canActivate: [authGuard],
        children: [
            { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
            { path: 'dashboard', component: DashboardComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator', 'Auditor'] } },
            { path: 'issuer/customers', component: CustomerListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'issuer/customers/:id', component: CustomerDetailComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'issuer/accounts', component: AccountListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'issuer/cards', component: CardListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'issuer/cards/:id', component: CardDetailComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'finance/ledger', component: LedgerListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator', 'Auditor'] } },
            { path: 'finance/billing', component: BillingStatementComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator', 'Auditor'] } },
            { path: 'finance/settlements', component: SettlementListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Auditor'] } },
            { path: 'finance/installments', component: InstallmentListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'switch/audit', component: AuditListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Auditor'] } },
            { path: 'switch/simulator', component: SimulatorComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'switch/catalogs', component: CatalogsComponent, canActivate: [roleGuard], data: { roles: ['Admin'] } },
            { path: 'switch/routing', component: RoutingComponent, canActivate: [roleGuard], data: { roles: ['Admin'] } },
            { path: 'security/vault', component: VaultComponent, canActivate: [roleGuard], data: { roles: ['Admin'] } },
            { path: 'security/rules', component: AntifraudListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'security/ecommerce', component: EcommerceMonitorComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator', 'Auditor'] } },
            { path: 'security/disputes', component: DisputeListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator', 'Auditor'] } },
            { path: 'admin/users', component: UsersComponent, canActivate: [roleGuard], data: { roles: ['Admin'] } },
            { path: 'admin/roles', component: RolesComponent, canActivate: [roleGuard], data: { roles: ['Admin'] } },
            // Sprint 9 — Ecosistema, Conectividad e Inteligencia
            { path: 'finance/accounting', component: AccountingListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Auditor'] } },
            { path: 'finance/analytics', component: AnalyticsDashboardComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Auditor'] } },
            { path: 'finance/credit-limits', component: CreditLimitListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'ecosystem/loyalty', component: LoyaltyListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'ecosystem/wallets', component: WalletsListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Operator'] } },
            { path: 'ecosystem/notifications', component: NotificationsListComponent, canActivate: [roleGuard], data: { roles: ['Admin', 'Auditor'] } },
            { path: 'ecosystem/open-banking', component: OpenBankingListComponent, canActivate: [roleGuard], data: { roles: ['Admin'] } },
        ]
    },
    { path: '**', redirectTo: 'auth/login' }
];
