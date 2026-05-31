import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, of } from 'rxjs';
import { CardDetailComponent } from './card-detail.component';
import { CardService, CardStatus, Card } from './card.service';
import { CustomerService } from '../customers/customer.service';
import { NotificationService } from '../../../core/notification.service';

const MOCK_CARD: Card = {
  id: 'test-card-001',
  accountId: 'acc-001',
  bin: '411111',
  panToken: 'tok_test',
  maskedPan: '411111******1111',
  expiryYyMm: '2812',
  last4: '1111',
  status: CardStatus.Active,
  createdOn: '2026-01-01T00:00:00Z'
};

describe('CardDetailComponent — lifecycle methods (RED → GREEN)', () => {
  let component: CardDetailComponent;
  let cardServiceSpy: jasmine.SpyObj<CardService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let notifSpy: jasmine.SpyObj<NotificationService>;

  beforeEach(() => {
    cardServiceSpy = jasmine.createSpyObj('CardService', [
      'getCard', 'blockCard', 'activateCard', 'unblockCard', 'cancelCard', 'replaceCard', 'setPin'
    ]);
    cardServiceSpy.getCard.and.returnValue(EMPTY);

    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    notifSpy = jasmine.createSpyObj('NotificationService', ['success', 'error', 'warning']);

    TestBed.configureTestingModule({
      imports: [CardDetailComponent],
      providers: [
        { provide: CardService, useValue: cardServiceSpy },
        { provide: CustomerService, useValue: { getCustomers: () => EMPTY } },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } }
        },
        { provide: NotificationService, useValue: notifSpy }
      ]
    });

    const fixture = TestBed.createComponent(CardDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  // ─── toggleBlock when Active ─────────────────────────────────────────────

  it('toggleBlock() when Active should call blockCard (existing correct behaviour)', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Active };
    cardServiceSpy.blockCard.and.returnValue(of({}));

    component.toggleBlock();

    expect(cardServiceSpy.blockCard).toHaveBeenCalledWith('test-card-001');
    expect(cardServiceSpy.unblockCard).not.toHaveBeenCalled();
    expect(cardServiceSpy.activateCard).not.toHaveBeenCalled();
  });

  // ─── toggleBlock when Blocked ─────────────────────────────────────────────

  // RED: fails because current code calls activateCard, not unblockCard
  it('toggleBlock() when Blocked should call unblockCard, NOT activateCard', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Blocked };
    cardServiceSpy.unblockCard.and.returnValue(of({}));

    component.toggleBlock();

    expect(cardServiceSpy.unblockCard).toHaveBeenCalledWith('test-card-001');
    expect(cardServiceSpy.activateCard).not.toHaveBeenCalled();
  });

  it('toggleBlock() when Blocked should set card status to Active on success', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Blocked };
    cardServiceSpy.unblockCard.and.returnValue(of({}));

    component.toggleBlock();

    expect(component.card!.status).toBe(CardStatus.Active);
    expect(notifSpy.success).toHaveBeenCalled();
  });

  // ─── cancelCard ──────────────────────────────────────────────────────────

  // RED: fails because current code calls blockCard, not cancelCard
  it('cancelCard() after confirm should call cardService.cancelCard, NOT blockCard', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Active };
    cardServiceSpy.cancelCard.and.returnValue(of({}));
    spyOn(window, 'confirm').and.returnValue(true);

    component.cancelCard();

    expect(cardServiceSpy.cancelCard).toHaveBeenCalledWith('test-card-001');
    expect(cardServiceSpy.blockCard).not.toHaveBeenCalled();
  });

  it('cancelCard() should set card status to Cancelled on success', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Active };
    cardServiceSpy.cancelCard.and.returnValue(of({}));
    spyOn(window, 'confirm').and.returnValue(true);

    component.cancelCard();

    expect(component.card!.status).toBe(CardStatus.Cancelled);
  });

  it('cancelCard() should NOT call service when confirm is dismissed', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Active };
    spyOn(window, 'confirm').and.returnValue(false);

    component.cancelCard();

    expect(cardServiceSpy.cancelCard).not.toHaveBeenCalled();
    expect(cardServiceSpy.blockCard).not.toHaveBeenCalled();
  });

  // ─── replaceCard ─────────────────────────────────────────────────────────

  // RED: fails because replaceCard method doesn't exist on component yet
  it('replaceCard() after confirm should call cardService.replaceCard', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Active };
    cardServiceSpy.replaceCard.and.returnValue(of({ newCardId: 'new-card-id' }));
    spyOn(window, 'confirm').and.returnValue(true);

    component.replaceCard();

    expect(cardServiceSpy.replaceCard).toHaveBeenCalledWith('test-card-001');
  });

  // GAP-3 (RED): Component used newCard.id but API returns { newCardId }.
  // Fails until card-detail.component.ts uses newCard.newCardId for navigation.
  it('replaceCard() should navigate to the new card using newCardId from response', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Active };
    cardServiceSpy.replaceCard.and.returnValue(of({ newCardId: 'new-card-id' }));
    spyOn(window, 'confirm').and.returnValue(true);

    component.replaceCard();

    expect(routerSpy.navigate).toHaveBeenCalledWith(['/app/issuer/cards/new-card-id']);
  });

  it('replaceCard() should NOT call service when confirm is dismissed', () => {
    component.card = { ...MOCK_CARD, status: CardStatus.Active };
    spyOn(window, 'confirm').and.returnValue(false);

    component.replaceCard();

    expect(cardServiceSpy.replaceCard).not.toHaveBeenCalled();
  });
});
