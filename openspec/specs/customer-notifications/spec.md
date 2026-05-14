# Customer Notifications Specification

## Purpose

Define how CardVault creates, dispatches, and exposes customer-facing transaction notifications and security alerts.

## Requirements

### Requirement: Transaction Notifications
The system SHALL create customer-facing notifications for card transaction activity using PCI-safe content.

#### Scenario: Transaction activity creates a notification
- WHEN CardVault materializes a switch transaction that should notify the customer
- THEN CardVault stores a transaction notification for the affected customer and account
- AND the notification content includes the amount and merchant context without exposing raw PAN

### Requirement: Security Alerts
The system MUST create customer-facing security alerts for suspicious card activity.

#### Scenario: Suspicious ecommerce authentication triggers a security alert
- WHEN CardVault detects suspicious ecommerce authentication activity such as a high-risk 3DS challenge or repeated OTP failures
- THEN CardVault stores a security alert notification for the affected customer
- AND the alert is eligible for delivery through the configured channels

### Requirement: Asynchronous Delivery Tracking
The system SHALL dispatch notification deliveries asynchronously and retain per-channel status.

#### Scenario: Notification deliveries are processed after persistence
- WHEN CardVault creates a notification
- THEN CardVault persists the notification and delivery records before dispatching them
- AND CardVault records whether each channel delivery is pending, sent, or failed

### Requirement: Notification History Visibility
The system MUST expose operational notification history without leaking sensitive payment data.

#### Scenario: Operators query notification history
- WHEN an authorized caller requests notification history
- THEN CardVault returns notification type, message summary, delivery status, timestamps, and customer or account references
- AND the response excludes raw PAN, OTP values, and unmasked destination data
