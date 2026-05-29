import { Component, Input, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { DelinquencyService } from './delinquency.service';
import { catchError, EMPTY } from 'rxjs';

@Component({
  selector: 'app-note-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="card p-4 mb-3" data-testid="note-form-card">
      <h3 class="form-title">Agregar Nota Interna</h3>
      <form [formGroup]="form" (ngSubmit)="onSubmit()" data-testid="note-form-inner">
        <div class="form-group">
          <label class="form-label">Contenido * (máx. 1000 caracteres)</label>
          <textarea class="form-input" formControlName="content" rows="4"
                    placeholder="Nota interna sobre este registro de mora..."></textarea>
          <div *ngIf="form.get('content')?.hasError('required') && form.get('content')?.touched"
               class="form-error" data-testid="content-required-error">
            El contenido es obligatorio.
          </div>
          <div *ngIf="form.get('content')?.hasError('maxlength')"
               class="form-error" data-testid="content-maxlength-error">
            El contenido no puede superar los 1000 caracteres.
          </div>
        </div>
        <div *ngIf="submitError" class="form-error mt-2" data-testid="submit-error">{{ submitError }}</div>
        <button type="submit" class="btn btn-primary mt-3" [disabled]="form.invalid || submitting">
          {{ submitting ? 'Guardando...' : 'Agregar Nota' }}
        </button>
      </form>
    </div>
  `,
  styles: [`
    .form-title { font-size: 1rem; font-weight: 600; margin: 0 0 1rem; }
    .form-group { margin-bottom: 0.5rem; }
    .form-label { display: block; font-size: 0.8rem; font-weight: 600; color: var(--text-muted); margin-bottom: 0.4rem; text-transform: uppercase; }
    .form-input { width: 100%; padding: 0.5rem 0.75rem; border: 1px solid var(--border-color); border-radius: var(--radius-sm); font-size: 0.875rem; }
    .form-error { font-size: 0.75rem; color: var(--danger, #dc2626); margin-top: 0.25rem; }
  `]
})
export class NoteFormComponent {
  @Input() delinquencyRecordId!: string;
  @Output() submitted = new EventEmitter<void>();

  private fb = inject(FormBuilder);
  private service = inject(DelinquencyService);

  submitting = false;
  submitError: string | null = null;

  form = this.fb.group({
    content: ['', [Validators.required, Validators.maxLength(1000)]],
  });

  onSubmit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.submitting = true;
    this.submitError = null;

    this.service.addNote(this.delinquencyRecordId, this.form.value.content!)
      .pipe(catchError(err => {
        this.submitError = err?.error?.error ?? 'Error al agregar la nota.';
        this.submitting = false;
        return EMPTY;
      })).subscribe(() => {
        this.submitting = false;
        this.form.reset();
        this.submitted.emit();
      });
  }
}
