import { LegalPage, legalMetadata } from "@/components/site/legal-page";
import { legalDocs } from "@/content/legal";

export const metadata = legalMetadata(legalDocs.gizlilik);

export default function PrivacyPage() {
  return <LegalPage doc={legalDocs.gizlilik} />;
}
