import { Component, Input, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { DelinquencyService } from './delinquency.service';
import { catchError, EMPTY } from 'rxjs';

const CHANNELS = ['Phone', 'Email', 'SMS', 'InPerson'] as const;
const OUTCOMES = ['Contacted', 'NoAnswer', 'InvalidContact', 'CustomerRefused'] as const;

@Component({
  selector: 'app-contact-attempt-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="card p-4 mb-3" data-testid="contact-form-card">
      <h3 class="form-title">Registrar Intento de Contacto</h3>
      <form [formGroup]="form" (ngSubmit)="onSubmit()" data-testid="contact-attempt-form-inner">
        <div class="form-row">
          <div class="form-group">
            <label class="form-label">Canal *</label>
            <select class="form-input" formControlName="channel">
              <option value="">Seleccioná un canal</option>
              <option *ngFor="let c of channels" [value]="c">{{ c }}</option>
            </select>
            <div *ngIf="form.get('channel')?.invalid && form.get('channel')?.touched"
                 class="form-error" data-testid="channel-error">
              Canal requerido.
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">Resultado *</label>
            <select class="form-input" formControlName="outcome">
              <option value="">Seleccioná un resultado</option>
              <option *ngFor="let o of outcomes" [value]="o">{{ o }}</option>
            </select>
            <div *ngIf="form.get('outcome')?.invalid && form.get('outcome')?.touched"
                 class="form-error" data-testid="outcome-error">
              Resultado requerido.
            </div>
          </div>
        </div>
        <div class="form-group mt-3">
          <label class="form-label">Notas (opcional, máx. 1000 caracteres)</label>
          <textarea class="form-input" formControlName="notes" rows="3"
                    placeholder="Observaciones sobre el contacto..."></textarea>
          <div *ngIf="form.get('notes')?.hasError('maxlength')"
               class="form-error" data-testid="notes-maxlength-error">
            Las notas no pueden superar los 1000 caracteres.
          </div>
        </div>
        <div *ngIf="submitError" class="form-error mt-2" data-testid="submit-error">{{ submitError }}</div>
        <button type="submit" class="btn btn-primary mt-3" [disabled]="form.invalid || submitting">
          {{ submitting ? 'Guardando...' : 'Registrar' }}
        </button>
      </form>
    </div>
  `,
  styles: [`
    .form-title { font-size: 1rem; font-weight: 600; margin: 0 0 1rem; }
    .form-row { display: flex; gap: 1rem; }
    .form-row .form-group { flex: 1; }
    .form-group { margin-bottom: 0.5rem; }
    .form-label { display: block; font-size: 0.8rem; font-weight: 600; color: var(--text-muted); margin-bottom: 0.4rem; text-transform: uppercase; }
    .form-input { width: 100%; padding: 0.5rem 0.75rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; }
    .form-error { font-size: 0.75rem; color: var(--danger, #dc2626); margin-top: 0.25rem; }
  `]
})
export class ContactAttemptFormComponent {
  @Input() delinquencyRecordId!: string;
  @Output() submitted = new EventEmitter<void>();

  private fb = inject(FormBuilder);
  private service = inject(DelinquencyService);

  channels = CHANNELS;
  outcomes = OUTCOMES;
  submitting = false;
  submitError: string | null = null;

  form = this.fb.group({
    channel: ['', Validators.required],
    outcome: ['', Validators.required],
    notes: ['', Validators.maxLength(1000)],
  });

  onSubmit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.submitting = true;
    this.submitError = null;
    const { channel, outcome, notes } = this.form.value;

    this.service.registerContactAttempt(
      this.delinquencyRecordId,
      channel!,
      outcome!,
      notes ?? undefined
    ).pipe(catchError(err => {
      this.submitError = err?.error?.error ?? 'Error al registrar el intento.';
      this.submitting = false;
      return EMPTY;
    })).subscribe(() => {
      this.submitting = false;
      this.form.reset();
      this.submitted.emit();
    });
  }
}
