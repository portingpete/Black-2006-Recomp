#include "kelvin_post_order.h"
#include <stdio.h>
#include <stdlib.h>

static unsigned checks;
#define CHECK(value) do { ++checks; if (!(value)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #value); exit(1); } } while (0)

int main(void)
{
    bool done = false;
    CHECK(!kg_post_order_should_apply_ao_before_blend(3, false, true, &done));
    CHECK(!done);
    CHECK(!kg_post_order_should_apply_ao_before_blend(4, true, true, &done));
    CHECK(!kg_post_order_should_apply_ao_before_blend(4, false, false, &done));
    CHECK(kg_post_order_should_apply_ao_before_blend(4, false, true, &done));
    CHECK(done);
    CHECK(!kg_post_order_should_apply_ao_before_blend(5, false, true, &done));
    printf("PASS: %u post-order checks\n", checks);
    return 0;
}
