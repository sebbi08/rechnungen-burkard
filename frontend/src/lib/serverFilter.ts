import { FilterService } from "@primevue/core/api";

/** The match mode of a Select whose filter field searches on the server. The
 *  Select filters its options a second time by what is typed, as one piece of
 *  text in one field, and would drop what the server found word by word:
 *  "Hanna Bauer" is not in "Bauer Hanna". Under this mode it shows the options
 *  it is given. */
export const SERVER_FILTER = "server";

FilterService.register(SERVER_FILTER, () => true);
