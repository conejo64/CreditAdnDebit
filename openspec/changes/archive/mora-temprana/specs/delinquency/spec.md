# Spec: Early Delinquency Management (Mora Temprana)

## 1. Overview
This specification defines the rules and behaviors for identifying and tracking credit card accounts that have failed to meet their minimum payment obligations by the due date.

## 2. Business Rules
*   **Rule 1 (Identification):** An account SHALL be considered delinquent if the current date is strictly greater than the `DueDate` of its most recent statement, AND the sum of payments applied to that statement is less than the `MinimumPayment`.
*   **Rule 2 (Status Change):** When an account becomes delinquent, its status MUST change to `DELINQUENT`.
*   **Rule 3 (Delinquency Tracking):** A `DelinquencyRecord` MUST be maintained for each delinquent cycle, tracking the `DaysInArrears` and the corresponding `Bucket`.
*   **Rule 4 (Buckets):** The delinquency buckets SHALL be classified as:
    *   `1_TO_30`: 1 to 30 days in arrears.
    *   `31_TO_60`: 31 to 60 days in arrears.
    *   `61_TO_90`: 61 to 90 days in arrears.
    *   `OVER_90`: >90 days in arrears.
*   **Rule 5 (Resolution):** If a delinquent account receives payments that cover the overdue minimum payment, the account status MUST return to `ACTIVE` and the `DelinquencyRecord` MUST be marked as `RESOLVED`.
*   **Rule 6 (Grace Period):** There is NO grace period. Day 1 of delinquency is the immediate day after the `DueDate`.

## 3. Scenarios

### Scenario 1: Unpaid minimum payment after due date
*   **Given** an `ACTIVE` credit card account with a generated statement
*   **And** the statement `MinimumPayment` is $50.00
*   **And** the total payments received is $0.00
*   **And** the `DueDate` was yesterday
*   **When** the delinquency evaluation runs
*   **Then** the account status MUST become `DELINQUENT`
*   **And** a new `DelinquencyRecord` MUST be created
*   **And** the record `DaysInArrears` MUST be 1
*   **And** the record `Bucket` MUST be `1_TO_30`
*   **And** the record `Status` MUST be `ACTIVE`

### Scenario 2: Partially paid minimum payment after due date
*   **Given** an `ACTIVE` credit card account with a generated statement
*   **And** the statement `MinimumPayment` is $50.00
*   **And** the total payments received is $49.99
*   **And** the `DueDate` was yesterday
*   **When** the delinquency evaluation runs
*   **Then** the account status MUST become `DELINQUENT`
*   **And** a `DelinquencyRecord` MUST be created with `OverdueAmount` = $0.01

### Scenario 3: Fully paid minimum payment
*   **Given** an `ACTIVE` credit card account with a generated statement
*   **And** the statement `MinimumPayment` is $50.00
*   **And** the total payments received is $50.00 (or more)
*   **And** the `DueDate` was yesterday
*   **When** the delinquency evaluation runs
*   **Then** the account status MUST remain `ACTIVE`
*   **And** NO `DelinquencyRecord` is created

### Scenario 4: Delinquency aging (crossing bucket threshold)
*   **Given** a `DELINQUENT` account with an active `DelinquencyRecord`
*   **And** the current `DaysInArrears` is 30 (`Bucket` = `1_TO_30`)
*   **And** the minimum payment remains unpaid
*   **When** the delinquency evaluation runs on the next day
*   **Then** the `DelinquencyRecord` `DaysInArrears` MUST increment to 31
*   **And** the `Bucket` MUST update to `31_TO_60`

### Scenario 5: Delinquency resolution
*   **Given** a `DELINQUENT` account with an active `DelinquencyRecord`
*   **When** a payment is processed that covers the remaining `OverdueAmount`
*   **Then** the account status MUST become `ACTIVE`
*   **And** the `DelinquencyRecord` `Status` MUST become `RESOLVED`
