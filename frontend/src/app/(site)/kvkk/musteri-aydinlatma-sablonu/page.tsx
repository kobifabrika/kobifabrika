import { LegalPage, legalMetadata } from "@/components/site/legal-page";
import { legalDocs } from "@/content/legal";

export const metadata = legalMetadata(legalDocs.sablon);

export default function CustomerNoticeTemplatePage() {
  return <LegalPage doc={legalDocs.sablon} />;
}
