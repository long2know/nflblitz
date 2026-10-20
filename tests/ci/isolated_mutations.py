"""Bounded execution for campaigns whose callers provide independent mutation/build trees."""
from concurrent.futures import ThreadPoolExecutor
import os


def run_mutations(cases, execute):
    cases = tuple(cases)
    labels = [case[0] for case in cases]
    if len(labels) != len(set(labels)):
        raise ValueError("Isolated mutation cases require unique output/source identities.")
    workers = os.environ.get("BLITZ_CI_MUTATION_WORKERS", "1")
    if workers not in ("1", "2"):
        raise ValueError("BLITZ_CI_MUTATION_WORKERS must be exactly 1 or 2.")
    if workers == "1":
        return [execute(case) for case in cases]
    # Drain all running work before callers can clean up their source/build trees.
    with ThreadPoolExecutor(max_workers=2) as pool:
        return list(pool.map(execute, cases))
