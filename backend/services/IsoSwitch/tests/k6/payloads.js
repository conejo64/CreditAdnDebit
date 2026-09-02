export function generateAuthorizeTransactionPayload() {
    const minStr = "000000";
    const maxStr = "999999";
    const traceId = String(Math.floor(Math.random() * 1000000)).padStart(6, '0');
    const bin = "411111";
    const pan = `411111${String(Math.floor(Math.random() * 10000000000)).padStart(10, '0')}`;
    const amount = (Math.random() * 1000).toFixed(2);
    
    // Generate ExpiryYyMm
    const year = String(new Date().getFullYear()).slice(-2);
    const month = String(Math.floor(Math.random() * 12) + 1).padStart(2, '0');
    const expiryYyMm = `${year}${month}`;

    return {
        TraceId: traceId,
        Bin: bin,
        Amount: amount,
        Pan: pan,
        ExpiryYyMm: expiryYyMm,
        Currency: "USD",
        MerchantId: `M${String(Math.floor(Math.random() * 10000)).padStart(4, '0')}`,
        ProcessingCode: "000000",
        Mti: "0100"
    };
}
