import { FetchClient } from "@odata2ts/http-client-fetch";
import { InvoicingService } from "./generated";

/** The generated OData client for everything the EDM describes. Relative base
 *  path: the dev server proxies /odata to whichever backend it points at. */
export const odata = new InvoicingService(new FetchClient(), "/odata");
