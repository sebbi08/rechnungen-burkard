import { type DocumentKindName, documentUrl, fetchDocuments } from "@/api/endpoints";

export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** Opens an invoice's PDF in a new tab. The tab is opened before the await:
 *  one opened afterwards no longer belongs to the click and is blocked as a
 *  popup. Throws after closing the tab when the lookup fails. */
export async function openDocument(invoiceId: number, kind: DocumentKindName): Promise<void> {
  const tab = window.open("", "_blank");
  try {
    const match = (await fetchDocuments(invoiceId)).find((document) => document.Kind === kind);
    if (!match) {
      tab?.close();
      return;
    }
    if (tab) {
      tab.location.href = documentUrl(match.Id);
    } else {
      // a browser that refused the empty tab may still allow a real URL
      window.open(documentUrl(match.Id), "_blank");
    }
  } catch (error) {
    tab?.close();
    throw error;
  }
}
