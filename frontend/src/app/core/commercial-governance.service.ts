import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, shareReplay, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export type CommercialMode = 'Commercial' | 'Demo';

export type ClaimMaturity = 'Verified' | 'Simulation' | 'Roadmap';

/** The sanitized shape served by GET /api/commercial/claims. */
export interface CommercialClaim {
    capabilityId: string;
    label: string;
    maturity: ClaimMaturity;
    permittedModes: CommercialMode[];
    commercialMessage: string;
}

export interface CommercialDisclosure {
    mode: CommercialMode;
    claimRegisterVersion: string;
    claims: CommercialClaim[];
}

/**
 * Single source of truth for what this deployment is allowed to show.
 *
 * The backend refuses simulator-backed traffic in commercial mode, so any screen
 * that would post such traffic has to know the mode before offering the action.
 * Asking after the fact leaves the operator staring at a rejection with no
 * explanation.
 */
@Injectable({ providedIn: 'root' })
export class CommercialGovernanceService {
    private readonly http = inject(HttpClient);

    // Commercial until proven otherwise. If the disclosure call fails - offline,
    // unauthorized, misconfigured - the safe answer is to withhold demo surfaces.
    // Revealing them because we could not reach the API is exactly the fake
    // availability this whole change exists to remove, and it mirrors the
    // server-side CommercialOptions default.
    private readonly disclosure = signal<CommercialDisclosure>({
        mode: 'Commercial',
        claimRegisterVersion: 'unknown',
        claims: []
    });

    private readonly loaded = signal(false);
    private request?: Observable<CommercialDisclosure>;

    readonly mode = computed(() => this.disclosure().mode);
    readonly claimRegisterVersion = computed(() => this.disclosure().claimRegisterVersion);
    readonly claims = computed(() => this.disclosure().claims);

    /** True while the mode is still the fail-closed assumption rather than a server answer. */
    readonly isResolved = computed(() => this.loaded());

    readonly isCommercial = computed(() => this.mode() === 'Commercial');

    /** Demo-only surfaces: simulators, synthetic message injection, sample data. */
    readonly canShowDemoSurfaces = computed(() => !this.isCommercial());

    /**
     * Loads the disclosure once per application lifetime. Errors are swallowed on
     * purpose: the fail-closed default already covers them, and a governance lookup
     * must never be able to break the screen that asked for it.
     */
    load(): Observable<CommercialDisclosure> {
        if (!this.request) {
            this.request = this.http
                .get<CommercialDisclosure>(`${environment.isoSwitchUrl}/commercial/claims`)
                .pipe(
                    tap(disclosure => {
                        this.disclosure.set(disclosure);
                        this.loaded.set(true);
                    }),
                    catchError(() => of(this.disclosure())),
                    shareReplay({ bufferSize: 1, refCount: false })
                );
        }

        return this.request;
    }

    /** The registered claim for a capability, when the register declares one. */
    claimFor(capabilityId: string): CommercialClaim | undefined {
        return this.claims().find(claim => claim.capabilityId === capabilityId);
    }

    /** Whether the running mode permits a capability the register knows about. */
    isCapabilityPermitted(capabilityId: string): boolean {
        const claim = this.claimFor(capabilityId);
        return claim ? claim.permittedModes.includes(this.mode()) : false;
    }

    /**
     * Why a capability is unavailable, in the register's own words when it has an
     * entry. The register is empty until governance publishes it, so a generic
     * fallback keeps the UI truthful rather than silent.
     */
    unavailabilityMessage(capabilityId: string): string {
        return (
            this.claimFor(capabilityId)?.commercialMessage ??
            'Esta funcionalidad no está disponible en modo comercial.'
        );
    }
}
