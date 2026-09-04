import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { CommercialGovernanceService, CommercialDisclosure } from './commercial-governance.service';
import { environment } from '../../environments/environment';

/**
 * Task 3.2: the governance service decides whether a screen may offer a
 * simulator-backed action. The backend already refuses that traffic in commercial
 * mode, so the only thing this service must never do is claim a surface is
 * available when it cannot prove it.
 */
describe('CommercialGovernanceService', () => {
    let service: CommercialGovernanceService;
    let httpMock: HttpTestingController;

    const url = `${environment.isoSwitchUrl}/commercial/claims`;

    const demoDisclosure: CommercialDisclosure = {
        mode: 'Demo',
        claimRegisterVersion: 'legacy-demo',
        claims: []
    };

    beforeEach(() => {
        TestBed.configureTestingModule({
            imports: [HttpClientTestingModule],
            providers: [CommercialGovernanceService]
        });
        service = TestBed.inject(CommercialGovernanceService);
        httpMock = TestBed.inject(HttpTestingController);
    });

    afterEach(() => httpMock.verify());

    it('assumes commercial mode before the disclosure has been loaded', () => {
        expect(service.isCommercial()).toBe(true);
        expect(service.canShowDemoSurfaces()).toBe(false);
        expect(service.isResolved()).toBe(false);
    });

    it('opens demo surfaces only once the server says the mode is Demo', () => {
        service.load().subscribe();
        httpMock.expectOne(url).flush(demoDisclosure);

        expect(service.mode()).toBe('Demo');
        expect(service.canShowDemoSurfaces()).toBe(true);
        expect(service.isResolved()).toBe(true);
    });

    it('keeps demo surfaces closed when the disclosure call fails', () => {
        // Revealing a simulator because the governance lookup was unreachable is
        // the fake availability this change exists to remove.
        service.load().subscribe();
        httpMock.expectOne(url).error(new ProgressEvent('network error'));

        expect(service.isCommercial()).toBe(true);
        expect(service.canShowDemoSurfaces()).toBe(false);
        expect(service.isResolved()).toBe(false);
    });

    it('keeps demo surfaces closed when the disclosure call is unauthorized', () => {
        service.load().subscribe();
        httpMock.expectOne(url).flush('', { status: 401, statusText: 'Unauthorized' });

        expect(service.canShowDemoSurfaces()).toBe(false);
    });

    it('requests the disclosure once however many callers ask for it', () => {
        service.load().subscribe();
        service.load().subscribe();

        // expectOne already fails on a second request, but assert the count
        // explicitly so the guarantee is visible in the spec rather than implied.
        const requests = httpMock.match(url);
        expect(requests.length).toBe(1);
        requests[0].flush(demoDisclosure);
    });

    it('reports a capability as permitted only when the register allows this mode', () => {
        service.load().subscribe();
        httpMock.expectOne(url).flush({
            mode: 'Commercial',
            claimRegisterVersion: '2026.08',
            claims: [
                {
                    capabilityId: 'switch.simulator',
                    label: 'Simulador de canales',
                    maturity: 'Simulation',
                    permittedModes: ['Demo'],
                    commercialMessage: 'Disponible únicamente en ambientes de demostración.'
                }
            ]
        } as CommercialDisclosure);

        expect(service.isCapabilityPermitted('switch.simulator')).toBe(false);
        expect(service.unavailabilityMessage('switch.simulator'))
            .toBe('Disponible únicamente en ambientes de demostración.');
    });

    it('treats a capability the register does not declare as not permitted', () => {
        service.load().subscribe();
        httpMock.expectOne(url).flush(demoDisclosure);

        // The register is empty until governance publishes it; an unknown capability
        // must not be waved through on the strength of its absence.
        expect(service.isCapabilityPermitted('switch.simulator')).toBe(false);
        expect(service.unavailabilityMessage('switch.simulator')).toContain('no está disponible');
    });
});
