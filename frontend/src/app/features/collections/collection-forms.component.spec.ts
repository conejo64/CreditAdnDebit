import { TestBed, ComponentFixture } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { ContactAttemptFormComponent } from './contact-attempt-form.component';
import { NoteFormComponent } from './note-form.component';
import { DelinquencyService } from './delinquency.service';

// ─────────────────────────────────────────────
// ContactAttemptFormComponent tests
// ─────────────────────────────────────────────

describe('ContactAttemptFormComponent', () => {
  let fixture: ComponentFixture<ContactAttemptFormComponent>;
  let component: ContactAttemptFormComponent;
  let serviceSpy: jasmine.SpyObj<DelinquencyService>;

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('DelinquencyService', ['registerContactAttempt']);
    serviceSpy.registerContactAttempt.and.returnValue(of({ id: 'new-id' }));

    await TestBed.configureTestingModule({
      imports: [ContactAttemptFormComponent],
      providers: [{ provide: DelinquencyService, useValue: serviceSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(ContactAttemptFormComponent);
    component = fixture.componentInstance;
    component.delinquencyRecordId = 'rec-test-1';
    fixture.detectChanges();
  });

  it('should be invalid when channel is empty', () => {
    component.form.setValue({ channel: '', outcome: 'Contacted', notes: '' });
    expect(component.form.get('channel')?.invalid).toBeTrue();
  });

  it('should be invalid when outcome is empty', () => {
    component.form.setValue({ channel: 'Phone', outcome: '', notes: '' });
    expect(component.form.get('outcome')?.invalid).toBeTrue();
  });

  it('should be valid with channel and outcome filled', () => {
    component.form.setValue({ channel: 'Phone', outcome: 'Contacted', notes: '' });
    expect(component.form.valid).toBeTrue();
  });

  it('should be invalid when notes exceed 1000 chars', () => {
    const longText = 'a'.repeat(1001);
    component.form.setValue({ channel: 'Phone', outcome: 'Contacted', notes: longText });
    expect(component.form.get('notes')?.hasError('maxlength')).toBeTrue();
  });

  it('should be valid with notes exactly 1000 chars', () => {
    const exactText = 'a'.repeat(1000);
    component.form.setValue({ channel: 'Phone', outcome: 'Contacted', notes: exactText });
    expect(component.form.valid).toBeTrue();
  });

  it('should call registerContactAttempt on valid submit', () => {
    component.form.setValue({ channel: 'SMS', outcome: 'NoAnswer', notes: 'No answer.' });
    component.onSubmit();
    expect(serviceSpy.registerContactAttempt).toHaveBeenCalledWith('rec-test-1', 'SMS', 'NoAnswer', 'No answer.');
  });

  it('should emit submitted event, call service, and reset form after successful POST', (done) => {
    component.form.setValue({ channel: 'Email', outcome: 'Contacted', notes: '' });
    component.submitted.subscribe(() => {
      expect(serviceSpy.registerContactAttempt).toHaveBeenCalledWith('rec-test-1', 'Email', 'Contacted', '');
      expect(component.form.pristine).toBeTrue();
      expect(component.submitting).toBeFalse();
      done();
    });
    component.onSubmit();
  });
});

// ─────────────────────────────────────────────
// NoteFormComponent tests
// ─────────────────────────────────────────────

describe('NoteFormComponent', () => {
  let fixture: ComponentFixture<NoteFormComponent>;
  let component: NoteFormComponent;
  let serviceSpy: jasmine.SpyObj<DelinquencyService>;

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('DelinquencyService', ['addNote']);
    serviceSpy.addNote.and.returnValue(of({ id: 'new-note-id' }));

    await TestBed.configureTestingModule({
      imports: [NoteFormComponent],
      providers: [{ provide: DelinquencyService, useValue: serviceSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(NoteFormComponent);
    component = fixture.componentInstance;
    component.delinquencyRecordId = 'rec-test-2';
    fixture.detectChanges();
  });

  it('should be invalid when content is empty', () => {
    component.form.setValue({ content: '' });
    expect(component.form.get('content')?.invalid).toBeTrue();
  });

  it('should be invalid when content exceeds 1000 chars', () => {
    const longText = 'a'.repeat(1001);
    component.form.setValue({ content: longText });
    expect(component.form.get('content')?.hasError('maxlength')).toBeTrue();
  });

  it('should be valid with non-empty content within limit', () => {
    component.form.setValue({ content: 'A valid note.' });
    expect(component.form.valid).toBeTrue();
  });

  it('should call addNote on valid submit', () => {
    component.form.setValue({ content: 'Important observation.' });
    component.onSubmit();
    expect(serviceSpy.addNote).toHaveBeenCalledWith('rec-test-2', 'Important observation.');
  });

  it('should emit submitted event, call service, and reset form after successful POST', (done) => {
    component.form.setValue({ content: 'Test note.' });
    component.submitted.subscribe(() => {
      expect(serviceSpy.addNote).toHaveBeenCalledWith('rec-test-2', 'Test note.');
      expect(component.form.pristine).toBeTrue();
      expect(component.submitting).toBeFalse();
      done();
    });
    component.onSubmit();
  });
});
