import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

export interface CatalogBin {
    id: string;
    binStart: number;
    binEnd: number;
    brand: string;
    product: string;
    countryCode?: string;
    issuerName?: string;
    enabled: boolean;
}

export interface CatalogProduct {
    id: string;
    code: string;
    brand: string;
    productType: string;
    name: string;
    enabled: boolean;
}

export interface CatalogCountry {
    code: string;
    name: string;
    numericCode: string;
    currency: string;
    enabled: boolean;
}

@Injectable({
    providedIn: 'root'
})
export class CatalogService {
    private http = inject(HttpClient);

    // Enpoints mapped from CardVault.Api section 4
    getBins(): Observable<CatalogBin[]> {
        return this.http.get<CatalogBin[]>(`${environment.apiUrl}/catalog/bins`);
    }

    getProducts(): Observable<CatalogProduct[]> {
        return this.http.get<CatalogProduct[]>(`${environment.apiUrl}/catalog/card-products`);
    }

    getCountries(): Observable<CatalogCountry[]> {
        return this.http.get<CatalogCountry[]>(`${environment.apiUrl}/catalog/countries`);
    }

    createBin(payload: Omit<CatalogBin, 'id' | 'enabled'>): Observable<any> {
        return this.http.post(`${environment.apiUrl}/catalog/bins`, payload);
    }

    createProduct(payload: Omit<CatalogProduct, 'id' | 'enabled'>): Observable<any> {
        return this.http.post(`${environment.apiUrl}/catalog/card-products`, payload);
    }

    createCountry(payload: Omit<CatalogCountry, 'enabled'>): Observable<any> {
        return this.http.post(`${environment.apiUrl}/catalog/countries`, payload);
    }

    getDocumentTypes(): Observable<string[]> {
        return this.http.get<string[]>(`${environment.apiUrl}/catalog/document-types`);
    }

    getGenders(): Observable<string[]> {
        return this.http.get<string[]>(`${environment.apiUrl}/catalog/genders`);
    }

    getCities(): Observable<string[]> {
        return this.http.get<string[]>(`${environment.apiUrl}/catalog/cities`);
    }
}
