import { Component, Input, Output, EventEmitter, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-search-select',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="search-select-wrapper" [class.disabled]="disabled">
      <!-- Badge de seleccionado -->
      <div class="selected-badge" *ngIf="value" (click)="!disabled && toggleDropdown()">
        <div class="selected-info">
          <span class="main-text">{{ getLabel(selectedItem) }}</span>
          <span class="sub-text" *ngIf="subLabelKey && getSubLabel(selectedItem)"> • {{ getSubLabel(selectedItem) }}</span>
        </div>
        <button type="button" class="clear-btn" (click)="clearSelection($event)" *ngIf="!disabled">
          <span class="material-symbols-rounded">close</span>
        </button>
      </div>

      <!-- Buscador (cuando no hay selección) -->
      <div class="search-container" *ngIf="!value" (click)="!disabled && openDropdown()">
        <span class="material-symbols-rounded search-icon">search</span>
        <input 
          type="text" 
          class="form-control search-input" 
          [placeholder]="placeholder" 
          [(ngModel)]="searchTerm"
          (focus)="openDropdown()"
          (input)="openDropdown()"
          [disabled]="disabled">
        <span class="material-symbols-rounded dropdown-icon">expand_more</span>
      </div>

      <!-- Dropdown -->
      <div class="dropdown-menu" *ngIf="isOpen && !disabled">
        <div 
          class="dropdown-item" 
          *ngFor="let item of filteredOptions" 
          (click)="selectItem(item)">
          <div class="item-info">
            <span class="main-text" [innerHTML]="highlight(getLabel(item))"></span>
            <span class="sub-text" *ngIf="subLabelKey">{{ getSubLabel(item) }}</span>
          </div>
        </div>
        <div class="dropdown-item no-results" *ngIf="filteredOptions.length === 0">
          <span class="material-symbols-rounded">search_off</span> Sin resultados
        </div>
      </div>
    </div>
  `,
  styles: [`
    .search-select-wrapper {
      position: relative;
      width: 100%;
    }
    .search-select-wrapper.disabled {
      opacity: 0.6;
      pointer-events: none;
    }
    .search-container {
      position: relative;
      display: flex;
      align-items: center;
      cursor: text;
    }
    .search-icon {
      position: absolute;
      left: 10px;
      font-size: 18px;
      color: var(--text-muted);
      pointer-events: none;
    }
    .dropdown-icon {
      position: absolute;
      right: 10px;
      font-size: 18px;
      color: var(--text-muted);
      pointer-events: none;
    }
    .search-input {
      padding-left: 2.2rem !important;
      padding-right: 2.2rem !important;
      width: 100%;
    }
    
    .selected-badge {
      display: flex;
      align-items: center;
      gap: 0.6rem;
      padding: 0.55rem 0.9rem;
      background: var(--bg-main);
      border: 1px solid var(--border-color);
      border-radius: var(--radius-md);
      cursor: pointer;
      min-height: 42px;
    }
    .selected-badge:hover {
      border-color: var(--primary);
    }
    .selected-info {
      flex: 1;
      display: flex;
      align-items: center;
      gap: 4px;
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }
    .selected-info .main-text {
      font-size: 0.875rem;
      font-weight: 500;
      color: var(--text-main);
    }
    .selected-info .sub-text {
      font-size: 0.75rem;
      color: var(--text-muted);
    }
    .clear-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 24px;
      height: 24px;
      background: rgba(239, 68, 68, 0.1);
      border: none;
      border-radius: 50%;
      cursor: pointer;
      color: var(--danger);
      transition: background 0.2s;
      flex-shrink: 0;
    }
    .clear-btn:hover {
      background: rgba(239, 68, 68, 0.2);
    }
    .clear-btn .material-symbols-rounded {
      font-size: 14px;
    }

    .dropdown-menu {
      position: absolute;
      top: calc(100% + 4px);
      left: 0;
      right: 0;
      background: var(--bg-paper);
      border: 1px solid var(--border-color);
      border-radius: var(--radius-md);
      box-shadow: var(--shadow-lg);
      max-height: 220px;
      overflow-y: auto;
      z-index: 1000;
      display: block; /* override bootstrap */
      padding: 0.25rem 0;
      animation: fadeIn 0.15s ease;
    }
    .dropdown-item {
      display: flex;
      align-items: center;
      padding: 0.6rem 0.9rem;
      cursor: pointer;
      border-bottom: 1px solid var(--bg-main);
      transition: background 0.15s;
    }
    .dropdown-item:last-child {
      border-bottom: none;
    }
    .dropdown-item:hover {
      background: var(--primary-light);
    }
    .dropdown-item.no-results {
      color: var(--text-muted);
      font-size: 0.85rem;
      font-style: italic;
      justify-content: center;
      gap: 0.5rem;
      cursor: default;
    }
    .dropdown-item.no-results:hover {
      background: transparent;
    }
    .item-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .item-info .main-text {
      font-size: 0.875rem;
      font-weight: 500;
      color: var(--text-main);
    }
    .item-info .sub-text {
      font-size: 0.75rem;
      color: var(--text-muted);
    }
    ::ng-deep .main-text mark {
      background: #fef08a;
      color: inherit;
      border-radius: 2px;
      padding: 0 1px;
    }
    
    @keyframes fadeIn {
      from { opacity: 0; transform: translateY(-5px); }
      to { opacity: 1; transform: translateY(0); }
    }
  `]
})
export class SearchSelectComponent {
  @Input() options: any[] = [];
  @Input() value: any = null;
  @Input() labelKey: string = 'name';
  @Input() subLabelKey: string = '';
  @Input() valueKey: string = 'id';
  @Input() placeholder: string = 'Buscar...';
  @Input() disabled: boolean = false;
  
  @Output() valueChange = new EventEmitter<any>();

  searchTerm = '';
  isOpen = false;

  constructor(private eRef: ElementRef) {}

  @HostListener('document:click', ['$event'])
  clickOut(event: Event) {
    if (!this.eRef.nativeElement.contains(event.target)) {
      this.isOpen = false;
    }
  }

  get selectedItem() {
    return this.options.find(o => this.getVal(o) === this.value);
  }

  get filteredOptions() {
    if (!this.searchTerm) return this.options.slice(0, 50); // limit for perf
    const q = this.searchTerm.toLowerCase().trim();
    return this.options.filter(o => 
      this.getLabel(o).toLowerCase().includes(q) || 
      (this.subLabelKey && this.getSubLabel(o).toLowerCase().includes(q))
    ).slice(0, 50);
  }

  getLabel(item: any): string {
    if (!item) return '';
    // handle nested keys like "customer.name" if needed, but for now simple
    return item[this.labelKey] || '';
  }

  getSubLabel(item: any): string {
    if (!item || !this.subLabelKey) return '';
    return item[this.subLabelKey] || '';
  }

  getVal(item: any): any {
    if (!item) return null;
    return item[this.valueKey];
  }

  openDropdown() {
    if (this.disabled) return;
    this.isOpen = true;
  }

  toggleDropdown() {
    if (this.disabled) return;
    if (this.isOpen) {
      this.isOpen = false;
    } else {
      this.searchTerm = '';
      this.isOpen = true;
    }
  }

  selectItem(item: any) {
    this.value = this.getVal(item);
    this.valueChange.emit(this.value);
    this.isOpen = false;
    this.searchTerm = '';
  }

  clearSelection(event: Event) {
    event.stopPropagation();
    this.value = null;
    this.valueChange.emit(this.value);
    this.searchTerm = '';
    // Optional: open dropdown after clear
    // this.isOpen = true;
  }

  highlight(text: string): string {
    const q = this.searchTerm.trim();
    if (!q) return text;
    const re = new RegExp(`(${q.replace(/[.*+?^${}()|[\\]\\\\]/g, '\\\\$&')})`, 'gi');
    return text.replace(re, '<mark>$1</mark>');
  }
}
