# Test data

These are regression inputs, not player collections or full traffic captures.
Merchant stock IDs and dates are synthetic; item mappings and prices preserve
the checked cases. Nearby appearances are minimal synthetic messages. Navigation
tests retain reduced geometry, replace actor identifiers, and zero opaque buff
fields. Minimap files contain only numeric points and boolean masks. Catalog
fixtures contain public game identifiers and source relationships.

Passing these fixtures checks the supported parser behavior. It does not prove
that an unobserved protocol form works in game. In particular, first-unlock
recognition is still incomplete.
